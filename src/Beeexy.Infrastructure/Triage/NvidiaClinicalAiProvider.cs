using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;

namespace Beeexy.Infrastructure.Triage;

public sealed class NvidiaClinicalAiProvider(
    HttpClient httpClient,
    NvidiaClinicalAiOptions options) : IClinicalAiProvider
{
    public const string HttpClientName = "NvidiaClinicalAi";
    private const int MaximumCompletionTokens = 512;

    public async Task<ClinicalAiProviderOutput> InterpretAsync(
        ClinicalAiInterpretationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);
        using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    model = options.Model,
                    messages = new[]
                    {
                        new { role = "system", content = Phase4ClinicalAiExtractionPrompt.SystemMessage(request) },
                        new { role = "user", content = Phase4ClinicalAiExtractionPrompt.UserMessage(request) }
                    },
                    temperature = 0.0,
                    max_tokens = MaximumCompletionTokens,
                    stream = false,
                    response_format = new { type = "json_object" },
                    chat_template_kwargs = new { enable_thinking = false }
                }),
                Encoding.UTF8,
                "application/json")
        };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ClinicalAiProviderException(ClinicalAiProviderFailureCategory.Timeout);
        }
        catch (HttpRequestException)
        {
            throw new ClinicalAiProviderException(ClinicalAiProviderFailureCategory.Unavailable);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new ClinicalAiProviderException(FailureFor(response.StatusCode));
            }

            string responseJson;
            try
            {
                responseJson = await response.Content.ReadAsStringAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ClinicalAiProviderException(ClinicalAiProviderFailureCategory.Timeout);
            }

            try
            {
                return ParseChatCompletion(responseJson);
            }
            catch (JsonException)
            {
                throw new ClinicalAiProviderException(
                    ClinicalAiProviderFailureCategory.InvalidStructuredResponse);
            }
            catch (InvalidStructuredResponseException)
            {
                throw new ClinicalAiProviderException(
                    ClinicalAiProviderFailureCategory.InvalidStructuredResponse);
            }
        }
    }

    private static ClinicalAiProviderFailureCategory FailureFor(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500
            ? ClinicalAiProviderFailureCategory.Unavailable
            : ClinicalAiProviderFailureCategory.RejectedOutput;

    private static ClinicalAiProviderOutput ParseChatCompletion(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        var choices = RequiredArray(root, "choices");
        if (choices.GetArrayLength() == 0)
        {
            throw new InvalidStructuredResponseException();
        }

        var choice = choices[0];
        var message = RequiredObject(choice, "message");
        var content = RequiredString(message, "content");
        return ParseProviderOutput(content);
    }

    private static ClinicalAiProviderOutput ParseProviderOutput(string content)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        RequireExactProperties(
            root,
            "schemaVersion",
            "intent",
            "pathwayCandidate",
            "facts",
            "symptoms",
            "ambiguities",
            "requiresClarification");

        var facts = RequiredArray(root, "facts").EnumerateArray()
            .Select(ParseFact)
            .ToArray();
        var symptoms = RequiredArray(root, "symptoms").EnumerateArray()
            .Select(ParseSymptom)
            .ToArray();
        var ambiguities = RequiredArray(root, "ambiguities").EnumerateArray()
            .Select(ParseAmbiguity)
            .ToArray();

        return new ClinicalAiProviderOutput(
            RequiredString(root, "schemaVersion"),
            ParseIntent(RequiredString(root, "intent")),
            RequiredString(root, "pathwayCandidate"),
            facts,
            symptoms,
            ambiguities,
            RequiredBoolean(root, "requiresClarification"),
            []);
    }

    private static ClinicalAiFactCandidate ParseFact(JsonElement element)
    {
        RequireExactProperties(element, "code", "value", "confidence");
        var code = RequiredString(element, "code");
        var value = RequiredObject(element, "value");
        ClinicalAiCandidateValue candidate = code switch
        {
            "DURATION" => ParseDuration(value),
            "INTENSITY" => ParseInteger(value),
            "ADDITIONAL_SYMPTOMS" => ParseAdditionalSymptoms(value),
            _ => throw new InvalidStructuredResponseException()
        };
        return new ClinicalAiFactCandidate(
            QuestionCode.Create(code),
            candidate,
            ParseConfidence(RequiredString(element, "confidence")));
    }

    private static ClinicalAiDurationValue ParseDuration(JsonElement value)
    {
        RequireExactProperties(value, "value", "unit");
        if (!value.GetProperty("value").TryGetDecimal(out var amount))
        {
            throw new InvalidStructuredResponseException();
        }

        return new ClinicalAiDurationValue(
            amount,
            RequiredString(value, "unit") switch
            {
                "MINUTES" => ClinicalDurationUnit.Minutes,
                "HOURS" => ClinicalDurationUnit.Hours,
                "DAYS" => ClinicalDurationUnit.Days,
                "WEEKS" => ClinicalDurationUnit.Weeks,
                "MONTHS" => ClinicalDurationUnit.Months,
                _ => throw new InvalidStructuredResponseException()
            });
    }

    private static ClinicalAiIntegerValue ParseInteger(JsonElement value)
    {
        RequireExactProperties(value, "value");
        if (!value.GetProperty("value").TryGetInt32(out var amount))
        {
            throw new InvalidStructuredResponseException();
        }

        return new ClinicalAiIntegerValue(amount);
    }

    private static ClinicalAiMultipleChoiceValue ParseAdditionalSymptoms(JsonElement value)
    {
        RequireExactProperties(value, "values");
        var values = RequiredArray(value, "values").EnumerateArray()
            .Select(RequiredStringValue)
            .ToArray();
        if (values.Any(item => item is not "NAUSEA" and not "DIARRHEA" and not "FEVER"))
        {
            throw new InvalidStructuredResponseException();
        }

        return new ClinicalAiMultipleChoiceValue(values);
    }

    private static ClinicalAiSymptomCandidate ParseSymptom(JsonElement element)
    {
        RequireExactProperties(element, "text", "normalizedPathwayCandidate", "confidence");
        return new ClinicalAiSymptomCandidate(
            RequiredString(element, "text"),
            RequiredString(element, "normalizedPathwayCandidate"),
            ParseConfidence(RequiredString(element, "confidence")));
    }

    private static ClinicalAiAmbiguity ParseAmbiguity(JsonElement element)
    {
        RequireAllowedProperties(element, "kind", "factCode");
        var kind = RequiredString(element, "kind") switch
        {
            "PATHWAY" => ClinicalAiAmbiguityKind.Pathway,
            "FACT_VALUE" => ClinicalAiAmbiguityKind.FactValue,
            "CONFLICTING_FACTS" => ClinicalAiAmbiguityKind.ConflictingFacts,
            "INSUFFICIENT_CONTEXT" => ClinicalAiAmbiguityKind.InsufficientContext,
            _ => throw new InvalidStructuredResponseException()
        };
        QuestionCode? factCode = null;
        if (element.TryGetProperty("factCode", out var factCodeElement))
        {
            var value = RequiredStringValue(factCodeElement);
            if (value is not "DURATION" and not "INTENSITY" and not "ADDITIONAL_SYMPTOMS")
            {
                throw new InvalidStructuredResponseException();
            }

            factCode = QuestionCode.Create(value);
        }

        return new ClinicalAiAmbiguity(kind, factCode);
    }

    private static ClinicalIntentClassification ParseIntent(string value) => value switch
    {
        "PRE_TRIAGE_INPUT" => ClinicalIntentClassification.PreTriageInput,
        "OUT_OF_SCOPE" => ClinicalIntentClassification.OutOfScope,
        "PRESCRIPTION_REQUEST" => ClinicalIntentClassification.PrescriptionRequest,
        "PROHIBITED_MEDICAL_ADVICE" => ClinicalIntentClassification.ProhibitedMedicalAdvice,
        "POTENTIAL_PROMPT_INJECTION" => ClinicalIntentClassification.PotentialPromptInjection,
        "UNSUPPORTED_CLINICAL_REQUEST" => ClinicalIntentClassification.UnsupportedClinicalRequest,
        "AMBIGUOUS" => ClinicalIntentClassification.Ambiguous,
        _ => throw new InvalidStructuredResponseException()
    };

    private static ClinicalAiConfidenceSignal ParseConfidence(string value) => value switch
    {
        "SUFFICIENT" => ClinicalAiConfidenceSignal.Sufficient,
        "UNCERTAIN" => ClinicalAiConfidenceSignal.Uncertain,
        "LOW" => ClinicalAiConfidenceSignal.Low,
        "UNSPECIFIED" => ClinicalAiConfidenceSignal.Unspecified,
        _ => throw new InvalidStructuredResponseException()
    };

    private static JsonElement RequiredObject(JsonElement parent, string property) =>
        RequiredProperty(parent, property, JsonValueKind.Object);

    private static JsonElement RequiredArray(JsonElement parent, string property) =>
        RequiredProperty(parent, property, JsonValueKind.Array);

    private static string RequiredString(JsonElement parent, string property)
    {
        return RequiredStringValue(RequiredProperty(parent, property, JsonValueKind.String));
    }

    private static string RequiredStringValue(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new InvalidStructuredResponseException();
        }

        var value = element.GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidStructuredResponseException()
            : value;
    }

    private static bool RequiredBoolean(JsonElement parent, string property) =>
        RequiredProperty(parent, property, JsonValueKind.True, JsonValueKind.False).GetBoolean();

    private static JsonElement RequiredProperty(
        JsonElement parent,
        string property,
        params JsonValueKind[] kinds)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(property, out var value) ||
            !kinds.Contains(value.ValueKind))
        {
            throw new InvalidStructuredResponseException();
        }

        return value;
    }

    private static void RequireExactProperties(JsonElement element, params string[] expected)
    {
        RequireAllowedProperties(element, expected);
        if (element.EnumerateObject().Count() != expected.Length)
        {
            throw new InvalidStructuredResponseException();
        }
    }

    private static void RequireAllowedProperties(JsonElement element, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            element.EnumerateObject().Any(property => !allowed.Contains(
                property.Name,
                StringComparer.Ordinal)))
        {
            throw new InvalidStructuredResponseException();
        }
    }

    private sealed class InvalidStructuredResponseException : Exception;
}
