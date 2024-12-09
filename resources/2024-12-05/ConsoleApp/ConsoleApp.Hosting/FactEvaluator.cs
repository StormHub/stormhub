using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;

namespace ConsoleApp.Hosting;

internal sealed class FactEvaluator : ChatConversationEvaluator
{
    public sealed class EvaluationExpert(string ideal) : EvaluationContext
    {
        public string IdealAnswer { get; } = ideal;
    }
    
    protected override ValueTask<string> RenderEvaluationPromptAsync(
        ChatMessage? userRequest,
        ChatMessage modelResponse,
        IEnumerable<ChatMessage>? includedHistory,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        string? ideal = default;
        if (additionalContext?.OfType<EvaluationExpert>().FirstOrDefault() is { } context)
        {
            ideal = context.IdealAnswer;
        }

        if (string.IsNullOrEmpty(ideal))
        {
            throw new InvalidOperationException("Ideal answer required in the additional context.");
        }
        
        var input = userRequest?.Text ?? string.Empty;
        var completion = modelResponse.Text ?? string.Empty;
        
        // From OpenAI eval template
        // https://github.com/openai/evals/blob/a32c9826cd7d5d33d60a39b54fb96d1085498d9a/evals/registry/modelgraded/fact.yaml
        var prompt = 
            $$"""
              You are comparing a submitted answer to an expert answer on a given question. Here is the data:
              [BEGIN DATA]
              ************
              [Question]: {{input}}
              ************
              [Expert]: {{ideal}}
              ************
              [Submission]: {{completion}}
              ************
              [END DATA]
              
              Compare the factual content of the submitted answer with the expert answer. Ignore any differences in style, grammar, or punctuation.
              The submitted answer may either be a subset or superset of the expert answer, or it may conflict with it. Determine which case applies. Answer the question by selecting one of the following options:
              (A) The submitted answer is a subset of the expert answer and is fully consistent with it.
              (B) The submitted answer is a superset of the expert answer and is fully consistent with it.
              (C) The submitted answer contains all the same details as the expert answer.
              (D) There is a disagreement between the submitted answer and the expert answer.
              (E) The answers differ, but these differences don't matter from the perspective of factuality.
              
              Return a string of choices, e.g. "A" or "B" or "C" or "D" or "E"
              """;

        return new ValueTask<string>(prompt);
    }

    protected override ValueTask ParseEvaluationResponseAsync(
        string modelResponseForEvaluationPrompt,
        EvaluationResult result,
        ChatConfiguration chatConfiguration,
        CancellationToken cancellationToken)
    {
        if (!result.TryGet<NumericMetric>(MetricName, out var numericMetric))
        {
            throw new InvalidOperationException("NumericMetric type required");
        }
        
        /*
           choice_scores:
           "A": 0.4
           "B": 0.6
           "C": 1
           "D": 0
           "E": 1
        */
        
        var rating = modelResponseForEvaluationPrompt switch {
            "A" => EvaluationRating.Average,
            "B" => EvaluationRating.Good,
            "C" => EvaluationRating.Exceptional,
            "D" => EvaluationRating.Unacceptable,
            "E" => EvaluationRating.Exceptional,
            _ => EvaluationRating.Inconclusive,
        };
        
        result.AddDiagnosticToAllMetrics(
            new (EvaluationDiagnosticSeverity.Informational, modelResponseForEvaluationPrompt));
        numericMetric.Interpretation = 
            new (rating, failed: rating == EvaluationRating.Inconclusive);
        
        return ValueTask.CompletedTask;
    }
    
    protected override EvaluationResult InitializeResult() => new(new NumericMetric(MetricName));

    public override IReadOnlyCollection<string> EvaluationMetricNames => [ MetricName ];

    private const string MetricName = nameof(FactEvaluator);
    
    protected override bool IgnoresHistory => true;
}