using System.ComponentModel;
using System.Threading.Tasks;
using Azure;
using Microsoft.SemanticKernel;
using Microsoft.BotBuilderSamples;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Azure.AI.OpenAI;
using System.Collections.Generic;
using System;

namespace Plugins;
public class DALLEPlugin
{
    private readonly OpenAIClient _aoaiClient;
    private ITurnContext<IMessageActivity> _turnContext;

    public DALLEPlugin(ConversationData conversationData, ITurnContext<IMessageActivity> turnContext, OpenAIClient aoaiClient)
    {
        _aoaiClient = aoaiClient;
        _turnContext = turnContext;
    }



    [KernelFunction, Description("Generate images from descriptions.")]
    public async Task<string> GenerateImages(
    [Description("The description of the images to be generated")] string prompt,
    [Description("The number of images to generate. If not specified, I should use 1")] int n
    )
    {
        if (n <= 0)
        {
            n = 1; // Atleast 1 image will generate
        }

        // Send initial message about image generation process
        await _turnContext.SendActivityAsync($"Generating {n} images with the description \"{prompt}\"...");

        List<object> images = new();
        images.Add(new { type = "TextBlock", text = "Here are the generated images.", size = "large" });

        for (int i = 0; i < n; i++)
        {
            // Create one image at a time
            var imageGenerations = await _aoaiClient.GetImageGenerationsAsync(new ImageGenerationOptions
            {
                Prompt = prompt,
                Size = ImageSize.Size1024x1024,
                ImageCount = 1,  // Create one image per request
                DeploymentName = "Dalle3"
            });

            foreach (var img in imageGenerations.Value.Data)
            {
                images.Add(new { type = "Image", url = img.Url.AbsoluteUri });
            }
        }

        var adaptiveCardJson = new
        {
            type = "AdaptiveCard",
            version = "1.0",
            body = images
        };

        var adaptiveCardAttachment = new Microsoft.Bot.Schema.Attachment()
        {
            ContentType = "application/vnd.microsoft.card.adaptive",
            Content = adaptiveCardJson,
        };

        await _turnContext.SendActivityAsync(MessageFactory.Attachment(adaptiveCardAttachment));
        return $"{n} images were generated successfully and already sent to user.";
    }

}