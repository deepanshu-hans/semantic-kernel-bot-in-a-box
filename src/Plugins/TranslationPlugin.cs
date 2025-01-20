using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace Plugins;
public class TranslationPlugin
{
    private readonly string _location;
    private readonly string _apiKey;
    private readonly string _apiEndpoint;

    public TranslationPlugin(string apiKey, string apiEndpoint, string location = "eastus2")
    {
        _apiKey = apiKey;
        _apiEndpoint = apiEndpoint;
        _location = location;
    }

    [KernelFunction, Description("Generate text translation.")]
    public async Task<string> TranslateTextAsync(string text, string targetLanguage)
    {
        var client = new HttpClient();
        
        // Build request body for Azure Translation API
        var requestBody = new StringContent(
            "[{ \"Text\": \"" + text + "\" }]", 
            Encoding.UTF8, 
            "application/json"
        );
        
        // Set headers for the Azure API request
        client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _apiKey);
        client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Region", _location);
        
        // Send the request to the Azure Translation API
        var response = await client.PostAsync($"{_apiEndpoint}/translate?api-version=3.0&to={targetLanguage}", requestBody);

        if (response.IsSuccessStatusCode)
        {
            var translationResponse = await response.Content.ReadAsStringAsync();
            // Parse the JSON response to extract the translated text
            var translations = Newtonsoft.Json.JsonConvert.DeserializeObject<List<dynamic>>(translationResponse);
            var translatedText = translations[0].translations[0].text;
            return $"{translatedText}";
        }
        else
        {
            throw new Exception("Translation API call failed. Status code: " + response.StatusCode);
        }
    }
}
