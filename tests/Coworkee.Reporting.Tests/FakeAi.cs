using System.Collections.Concurrent;
using Coworkee.Application.Ai;

namespace Coworkee.Reporting.Tests;

/// <summary>Answers with the queued texts in order; without answers no model is configured.</summary>
public sealed class FakeAi : IAiTextGenerator
{
    private readonly ConcurrentQueue<string> _answers = new();

    public List<(string Instructions, string Input)> Calls { get; } = [];

    public void Answer(params string[] answers)
    {
        _answers.Clear();
        Calls.Clear();
        foreach (var answer in answers)
        {
            _answers.Enqueue(answer);
        }
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(!_answers.IsEmpty || Calls.Count > 0);

    public Task<string?> GenerateAsync(string instructions, string input, CancellationToken cancellationToken)
    {
        Calls.Add((instructions, input));
        return Task.FromResult(_answers.TryDequeue(out var answer) ? answer : null);
    }
}
