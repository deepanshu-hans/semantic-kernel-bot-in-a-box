// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Storage.Blobs;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Planning;
using Microsoft.SemanticKernel.Planning.Handlebars;
using Plugins;
using Services;

namespace Microsoft.BotBuilderSamples
{
    public class SemanticKernelBot<T> : DocumentUploadBot<T> where T : Dialog
    {
        private Kernel kernel;
        private string _aoaiModel;
        private string _dalleModel;
        private string _apiKey;
        private string _apiendpoint;
        private string _translationApiKey;
        private string _translationApiEndpoint;
        private readonly OpenAIClient _aoaiClient;
        private readonly BingClient _bingClient;
        private readonly SearchClient _searchClient;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly AzureOpenAITextEmbeddingGenerationService _embeddingsClient;
        private readonly DocumentAnalysisClient _documentAnalysisClient;
        private readonly SqlConnectionFactory _sqlConnectionFactory;
        private readonly string _welcomeMessage;
        private readonly List<string> _suggestedQuestions;
        private readonly bool _useStepwisePlanner;
        private readonly string _searchSemanticConfig;
        private readonly TranslationPlugin _translationPlugin;
        private readonly Dictionary<string, string> _supportedLanguages;

        public SemanticKernelBot(
            IConfiguration config,
            ConversationState conversationState,
            UserState userState,
            OpenAIClient aoaiClient,
            AzureOpenAITextEmbeddingGenerationService embeddingsClient,
            T dialog,
            DocumentAnalysisClient documentAnalysisClient = null,
            SearchClient searchClient = null,
            BlobServiceClient blobServiceClient = null,
            BingClient bingClient = null,
            SqlConnectionFactory sqlConnectionFactory = null) :
            base(config, conversationState, userState, embeddingsClient, documentAnalysisClient, dialog)
        {
            _aoaiModel = config.GetValue<string>("AOAI_GPT_MODEL");
            _dalleModel = config.GetValue<string>("AOAI_IMAGE_MODEL");
            _apiKey = config.GetValue<string>("AOAI_API_KEY");
            _apiendpoint = config.GetValue<string>("AOAI_API_ENDPOINT");
            _welcomeMessage = config.GetValue<string>("PROMPT_WELCOME_MESSAGE");
            _systemMessage = config.GetValue<string>("PROMPT_SYSTEM_MESSAGE");
            _suggestedQuestions = System.Text.Json.JsonSerializer.Deserialize<List<string>>(config.GetValue<string>("PROMPT_SUGGESTED_QUESTIONS"));
            _useStepwisePlanner = config.GetValue<bool>("USE_STEPWISE_PLANNER");
            _searchSemanticConfig = config.GetValue<string>("SEARCH_SEMANTIC_CONFIG");
            _translationApiKey = config.GetValue<string>("TRANSLATOR_API_KEY");
            _translationApiEndpoint = config.GetValue<string>("TRANSLATOR_API_ENDPOINT");
            _aoaiClient = aoaiClient;
            _searchClient = searchClient;
            _blobServiceClient = blobServiceClient;
            _bingClient = bingClient;
            _embeddingsClient = embeddingsClient;
            _documentAnalysisClient = documentAnalysisClient;
            _sqlConnectionFactory = sqlConnectionFactory;
            _translationPlugin = new TranslationPlugin(_translationApiKey, _translationApiEndpoint);
            // Load supported languages from appsettings.json
            _supportedLanguages = config.GetSection("SUPPORTED_LANGUAGES").Get<Dictionary<string, string>>();
        }

        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            await turnContext.SendActivityAsync(new Activity()
            {
                Type = "message",
                Text = _welcomeMessage,
                SuggestedActions = new SuggestedActions()
                {
                    Actions = _suggestedQuestions
                        .Select(value => new CardAction(type: "postBack", value: value))
                        .ToList()
                }
            });
        }

        public override async Task<string> ProcessMessage(ConversationData conversationData, ITurnContext<IMessageActivity> turnContext)
        {
            await turnContext.SendActivityAsync(new Activity(type: "typing"));

            await HandleFileUploads(conversationData, turnContext);

            if (turnContext.Activity.Text.IsNullOrEmpty())
                return "";

            string userInput = turnContext.Activity.Text.Trim();

            // Detect if the user is asking for a translation
            if (userInput.StartsWith("Translate", StringComparison.OrdinalIgnoreCase))
            {
                // Extract text and target language(s)
                string[] parts = userInput.Substring(10).Split(" to ", StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    string textToTranslate = parts[0].Trim();
                    string[] targetLanguages = parts[1].Split(',').Select(lang => lang.Trim()).ToArray();

                    try
                    {
                        // lookup: full name -> short form
                        var reverseSupportedLanguages = _supportedLanguages.ToDictionary(kv => kv.Value.ToLower(), kv => kv.Key.ToLower());

                        // Check if each target language is supported
                        List<string> results = new List<string>();
                        foreach (string targetLanguage in targetLanguages)
                        {
                            string targetLanguageLower = targetLanguage.ToLower();
                            string languageKey = null;

                            // Check if target language is full name (e.g., "German") or short form (e.g., "de")
                            if (reverseSupportedLanguages.ContainsKey(targetLanguageLower))
                            {
                                languageKey = reverseSupportedLanguages[targetLanguageLower]; // Get the short form
                            }
                            else if (_supportedLanguages.ContainsKey(targetLanguageLower))
                            {
                                languageKey = targetLanguageLower; // It is already a short form (e.g., "de")
                            }

                            if (languageKey == null)
                            {
                                results.Add($"The language '{targetLanguage}' is not supported.");
                            }
                            else
                            {
                                // Perform translation using the correct short form
                                string translatedText = await _translationPlugin.TranslateTextAsync(textToTranslate, languageKey);
                                results.Add($"Translated to {targetLanguage}: {translatedText}");
                            }
                        }

                        if (results.Count == 0)
                        {
                            return "No valid languages were specified.";
                        }

                        return string.Join("\n", results);
                    }
                    catch (Exception ex)
                    {
                        await turnContext.SendActivityAsync("Failed to translate text. Please ensure the input format is correct.");
                        return $"Error: {ex.Message}";
                    }
                }
                else
                {
                    await turnContext.SendActivityAsync("Invalid translation request. Use the format: 'Translate: [text] to [language1, language2,...]'.");
                    return "";
                }
            }

            if (userInput.Equals("Show languages", StringComparison.OrdinalIgnoreCase))
            {
                await DisplaySupportedLanguagesAsync(turnContext);
                return "Displayed supported languages.";
            }

            // Continue with the existing image generation and other logic if it's not a translation request
            kernel = Kernel.CreateBuilder()
                .AddAzureOpenAIChatCompletion(_aoaiModel, _aoaiClient)
                .AddAzureOpenAITextToImage(_dalleModel, _apiendpoint, _apiKey)
                .Build();

            // Import plugins as usual
            if (_sqlConnectionFactory != null) kernel.ImportPluginFromObject(new SQLPlugin(conversationData, turnContext, _sqlConnectionFactory), "SQLPlugin");
            if (_documentAnalysisClient != null) kernel.ImportPluginFromObject(new UploadPlugin(conversationData, turnContext, _embeddingsClient), "UploadPlugin");
            if (_searchClient != null) kernel.ImportPluginFromObject(new HRHandbookPlugin(conversationData, turnContext, _embeddingsClient, _searchClient, _blobServiceClient, _searchSemanticConfig), "HRHandbookPlugin");
            kernel.ImportPluginFromObject(new DALLEPlugin(conversationData, turnContext, _aoaiClient), "DALLEPlugin");
            if (_bingClient != null) kernel.ImportPluginFromObject(new BingPlugin(conversationData, turnContext, _bingClient), "BingPlugin");
            if (!_useStepwisePlanner) kernel.ImportPluginFromObject(new HumanInterfacePlugin(conversationData, turnContext, _aoaiClient), "HumanInterfacePlugin");

            // Process with stepwise planner if enabled
            if (_useStepwisePlanner)
            {
                var plannerOptions = new FunctionCallingStepwisePlannerConfig { MaxTokens = 128000 };
                var planner = new FunctionCallingStepwisePlanner(plannerOptions);
                string prompt = FormatConversationHistory(conversationData);
                var result = await planner.ExecuteAsync(kernel, prompt);
                return result.FinalAnswer;
            }
            else
            {
                var plannerOptions = new HandlebarsPlannerOptions { MaxTokens = 128000 };
                var planner = new HandlebarsPlanner(plannerOptions);
                string prompt = FormatConversationHistory(conversationData);
                var plan = await planner.CreatePlanAsync(kernel, prompt);
                var result = await plan.InvokeAsync(kernel, default);
                return result;
            }
        }

        public async Task<string> DisplaySupportedLanguagesAsync(ITurnContext<IMessageActivity> turnContext)
        {
            if (_supportedLanguages == null || !_supportedLanguages.Any())
            {
                string fallbackMessage = "No supported languages found in the configuration.";
                await turnContext.SendActivityAsync(fallbackMessage);
                return fallbackMessage;
            }

            string languagesList = string.Join("\n", _supportedLanguages.Select(lang => $"{lang.Value} ({lang.Key})"));
            string message = $"Supported languages:\n{languagesList}";
            await turnContext.SendActivityAsync(message);
            return message;
        }
    }
}
