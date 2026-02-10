using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoBananaProWinUI.Services;

public sealed class GeminiImageService
{
    private const int GenerationCount = 4;
    private const string ModelName = "gemini-3-pro-image-preview";
    private const int MaxNetworkAttempts = 3;
    private static readonly IReadOnlyList<string> SupportedAspectRatios =
    [
        "1:1",
        "2:3",
        "3:2",
        "3:4",
        "4:3",
        "4:5",
        "5:4",
        "9:16",
        "16:9",
        "21:9",
    ];

    private readonly HttpClient _httpClient;

    public GeminiImageService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<IReadOnlyList<string>> GenerateEditedImagesAsync(
        string apiKey,
        string base64Data,
        string mimeType,
        string prompt,
        string imageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Missing Gemini API key.");
        }

        var sourceBytes = Convert.FromBase64String(base64Data);
        var (sourceWidth, sourceHeight) = await ImageDataHelpers.GetImageDimensionsAsync(sourceBytes, cancellationToken);
        var aspectRatio = ResolveRequestedAspectRatio(sourceWidth, sourceHeight);

        var requestUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{ModelName}:generateContent?key={Uri.EscapeDataString(apiKey)}";

        var generationTasks = Enumerable.Range(0, GenerationCount)
            .Select(_ => GenerateSingleImageAsync(requestUrl, base64Data, mimeType, prompt, imageSize, aspectRatio, cancellationToken))
            .Select(WrapResultAsync);

        var settledResults = await Task.WhenAll(generationTasks);

        var imageUrls = settledResults.Where(result => result.Success && result.ImageDataUrl is not null)
            .Select(result => result.ImageDataUrl!)
            .ToList();

        if (imageUrls.Count > 0)
        {
            var normalizedResults = new List<string>(imageUrls.Count);
            foreach (var imageUrl in imageUrls)
            {
                var normalizedDataUrl = await ImageDataHelpers.EnsureDataUrlAspectRatioAsync(
                    imageUrl,
                    sourceWidth,
                    sourceHeight,
                    cancellationToken);
                normalizedResults.Add(normalizedDataUrl);
            }

            return normalizedResults;
        }

        var errors = settledResults
            .Where(result => result.Error is not null)
            .Select(result => result.Error!)
            .ToList();

        var detail = errors.Count == 0
            ? "Unknown error from Gemini API."
            : string.Join(" | ", errors.Select(static e => e.Message).Distinct().Take(3));

        throw new InvalidOperationException($"All image generation requests failed. Details: {detail}", errors.FirstOrDefault());
    }

    private async Task<string> GenerateSingleImageAsync(
        string requestUrl,
        string base64Data,
        string mimeType,
        string prompt,
        string imageSize,
        string aspectRatio,
        CancellationToken cancellationToken)
    {
        var generationConfigPayload = CreatePayloadJson(useConfigField: false, base64Data, mimeType, prompt, imageSize, aspectRatio);
        var response = await SendGenerateRequestAsync(requestUrl, generationConfigPayload, cancellationToken);

        if (!response.Success && IsUnknownPayloadFieldError(response.Body, "generationConfig"))
        {
            var configPayload = CreatePayloadJson(useConfigField: true, base64Data, mimeType, prompt, imageSize, aspectRatio);
            response = await SendGenerateRequestAsync(requestUrl, configPayload, cancellationToken);
        }

        if (!response.Success)
        {
            var message = ExtractApiErrorMessage(response.Body);
            throw new InvalidOperationException($"Gemini API error ({(int)response.StatusCode}): {message}");
        }

        var imageDataUrl = ExtractFirstImageDataUrl(response.Body);
        if (string.IsNullOrWhiteSpace(imageDataUrl))
        {
            throw new InvalidOperationException("The Gemini response did not include image content.");
        }

        return imageDataUrl;
    }

    private static string CreatePayloadJson(
        bool useConfigField,
        string base64Data,
        string mimeType,
        string prompt,
        string imageSize,
        string aspectRatio)
    {
        var payload = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["parts"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["inlineData"] = new JsonObject
                            {
                                ["data"] = base64Data,
                                ["mimeType"] = mimeType
                            }
                        },
                        new JsonObject
                        {
                            ["text"] = prompt
                        }
                    }
                }
            }
        };

        var configNode = new JsonObject
        {
            ["imageConfig"] = new JsonObject
            {
                ["imageSize"] = imageSize,
                ["aspectRatio"] = aspectRatio
            }
        };

        payload[useConfigField ? "config" : "generationConfig"] = configNode;
        return payload.ToJsonString();
    }

    private static string ResolveRequestedAspectRatio(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return "1:1";
        }

        var divisor = Gcd(width, height);
        var reducedWidth = width / divisor;
        var reducedHeight = height / divisor;
        var reducedRatio = $"{reducedWidth}:{reducedHeight}";

        if (SupportedAspectRatios.Any(candidate => string.Equals(candidate, reducedRatio, StringComparison.Ordinal)))
        {
            return reducedRatio;
        }

        var supported = string.Join(", ", SupportedAspectRatios);
        throw new InvalidOperationException(
            $"Source image aspect ratio {reducedRatio} is not supported by Gemini API. " +
            $"Supported ratios: {supported}. Generation cancelled to avoid spending credits on mismatched sizes.");
    }

    private static int Gcd(int left, int right)
    {
        left = Math.Abs(left);
        right = Math.Abs(right);
        while (right != 0)
        {
            var temp = left % right;
            left = right;
            right = temp;
        }

        return left == 0 ? 1 : left;
    }

    private async Task<ApiCallResult> SendGenerateRequestAsync(string requestUrl, string payloadJson, CancellationToken cancellationToken)
    {
        Exception? lastNetworkException = null;

        for (var attempt = 1; attempt <= MaxNetworkAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
                {
                    Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
                };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                return new ApiCallResult(
                    response.IsSuccessStatusCode,
                    response.StatusCode,
                    responseBody);
            }
            catch (HttpRequestException ex) when (IsTransientNetworkException(ex) && attempt < MaxNetworkAttempts)
            {
                lastNetworkException = ex;
                var retryDelay = TimeSpan.FromMilliseconds(400 * attempt);
                await Task.Delay(retryDelay, cancellationToken);
            }
        }

        if (lastNetworkException is not null)
        {
            throw new InvalidOperationException(
                "Network error reaching Gemini API. Check internet, DNS, firewall/proxy, then try again.",
                lastNetworkException);
        }

        throw new InvalidOperationException("Failed to send request to Gemini API.");
    }

    private static string? ExtractFirstImageDataUrl(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("candidates", out var candidatesElement) || candidatesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var candidate in candidatesElement.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var contentElement) || !contentElement.TryGetProperty("parts", out var partsElement))
            {
                continue;
            }

            foreach (var part in partsElement.EnumerateArray())
            {
                if (!part.TryGetProperty("inlineData", out var inlineDataElement))
                {
                    continue;
                }

                if (!inlineDataElement.TryGetProperty("data", out var dataElement))
                {
                    continue;
                }

                var data = dataElement.GetString();
                if (string.IsNullOrWhiteSpace(data))
                {
                    continue;
                }

                var mimeType = inlineDataElement.TryGetProperty("mimeType", out var mimeTypeElement)
                    ? mimeTypeElement.GetString() ?? "image/png"
                    : "image/png";

                return ImageDataHelpers.BuildDataUrl(mimeType, data);
            }
        }

        return null;
    }

    private static string ExtractApiErrorMessage(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("error", out var errorElement))
            {
                if (errorElement.TryGetProperty("message", out var messageElement))
                {
                    return messageElement.GetString() ?? responseBody;
                }
            }
        }
        catch (JsonException)
        {
            // fall through and return raw response body
        }

        return responseBody;
    }

    private static bool IsUnknownPayloadFieldError(string responseBody, string fieldName)
    {
        return responseBody.Contains("Unknown name", StringComparison.OrdinalIgnoreCase) &&
               responseBody.Contains(fieldName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransientNetworkException(HttpRequestException ex)
    {
        if (ex.InnerException is SocketException socketException)
        {
            return socketException.SocketErrorCode is SocketError.HostNotFound
                or SocketError.TryAgain
                or SocketError.NoData
                or SocketError.TimedOut
                or SocketError.NetworkDown
                or SocketError.NetworkUnreachable;
        }

        return ex.Message.Contains("No such host is known", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("Temporary failure in name resolution", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<GenerationResult> WrapResultAsync(Task<string> generationTask)
    {
        try
        {
            var result = await generationTask;
            return new GenerationResult(true, result, null);
        }
        catch (Exception ex)
        {
            return new GenerationResult(false, null, ex);
        }
    }

    private sealed record ApiCallResult(bool Success, HttpStatusCode StatusCode, string Body);

    private sealed record GenerationResult(bool Success, string? ImageDataUrl, Exception? Error);
}
