﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Azure.Search.Documents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI.TextEmbedding;

namespace Microsoft.BotBuilderSamples
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddEnvironmentVariables()
                .Build();
            services.AddSingleton(configuration);

            TokenCredential azureCredentials;
            if (configuration.GetValue<string>("MicrosoftAppType") == "UserAssignedMSI")
                azureCredentials = new DefaultAzureCredential(new DefaultAzureCredentialOptions() { ManagedIdentityClientId = configuration.GetValue<string>("MicrosoftAppId") });
            else
                azureCredentials = new DefaultAzureCredential();
            services.AddHttpClient().AddControllers().AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.MaxDepth = HttpHelper.BotMessageSerializerSettings.MaxDepth;
            });

            // Create the Bot Framework Authentication to be used with the Bot Adapter.
            services.AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>();

            // Create the Bot Adapter with error handling enabled.
            services.AddSingleton<IBotFrameworkHttpAdapter, AdapterWithErrorHandler>();

            IStorage storage;
            if (configuration.GetValue<string>("COSMOS_API_ENDPOINT") != null)
            {
                var cosmosDbStorageOptions = new CosmosDbPartitionedStorageOptions()
                {
                    CosmosDbEndpoint = configuration.GetValue<string>("COSMOS_API_ENDPOINT"),
                    TokenCredential = azureCredentials,
                    DatabaseId = "SemanticKernelBot",
                    ContainerId = "Conversations"
                };
                storage = new CosmosDbPartitionedStorage(cosmosDbStorageOptions);
            }
            else
            {
                storage = new MemoryStorage();
            }


            // Create the User state passing in the storage layer.
            var userState = new UserState(storage);
            services.AddSingleton(userState);

            // Create the Conversation state passing in the storage layer.
            var conversationState = new ConversationState(storage);
            services.AddSingleton(conversationState);

            services.AddSingleton(new OpenAIClient(new Uri(configuration.GetValue<string>("AOAI_API_ENDPOINT")), azureCredentials));
            services.AddSingleton(new AzureTextEmbeddingGeneration(configuration.GetValue<string>("AOAI_EMBEDDINGS_MODEL"), configuration.GetValue<string>("AOAI_API_ENDPOINT"), azureCredentials));
            if (!configuration.GetValue<string>("DOCINTEL_API_ENDPOINT").IsNullOrEmpty())
                services.AddSingleton(new DocumentAnalysisClient(new Uri(configuration.GetValue<string>("DOCINTEL_API_ENDPOINT")), azureCredentials));
            if (!configuration.GetValue<string>("SEARCH_API_ENDPOINT").IsNullOrEmpty())
                services.AddSingleton(new SearchClient(new Uri(configuration.GetValue<string>("SEARCH_API_ENDPOINT")), configuration.GetValue<string>("SEARCH_INDEX"), azureCredentials));
            if (!configuration.GetValue<string>("SQL_CONNECTION_STRING").IsNullOrEmpty())
                services.AddSingleton(new SqlConnectionFactory(configuration.GetValue<string>("SQL_CONNECTION_STRING")));

            services.AddHttpClient();
            // Create the bot as a transient. In this case the ASP Controller is expecting an IBot.
            services.AddTransient<IBot, SemanticKernelBot>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseDefaultFiles()
                .UseStaticFiles()
                .UseRouting()
                .UseAuthorization()
                .UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });

            // app.UseHttpsRedirection();
        }
    }
}
