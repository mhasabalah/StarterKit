using Agentic.Contracts.Agents;
using Agentic.Contracts.Tools;
using Agentic.Runtime.Tools;
using FluentValidation;
using Shared.Contracts.Localization;

namespace Agentic.Tests;

public sealed record EchoInput(string Text, int Times = 1);

public sealed record EchoOutput(string Text);

[AgentTool("test.echo", "Repeats a text.", AllowAnyMember = true)]
public sealed class EchoTool : IAgentTool<EchoInput, EchoOutput>
{
    public Task<EchoOutput> ExecuteAsync(EchoInput input, AgentToolContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new EchoOutput(string.Concat(Enumerable.Repeat(input.Text, input.Times))));
}

public sealed class EchoInputValidator : AbstractValidator<EchoInput>
{
    public EchoInputValidator()
    {
        RuleFor(x => x.Text).NotEmpty().WithMessage("Text is required.");
        RuleFor(x => x.Times).InclusiveBetween(1, 3).WithMessage("Times must be between 1 and 3.");
    }
}

public sealed record NoteInput(string Title);

public sealed record NoteOutput(Guid Id);

[AgentTool("test.read_document", "Reads a document.", Permission = TestPermissions.NotesRead, ReturnsUntrustedContent = true)]
public sealed class ReadDocumentTool : IAgentTool<NoteInput, EchoOutput>
{
    public Task<EchoOutput> ExecuteAsync(NoteInput input, AgentToolContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new EchoOutput("content"));
}

[AgentTool("test.create_note", "Creates a draft note.", Risk = AgentToolRisk.LowRiskMutation, Permission = TestPermissions.NotesWrite)]
public sealed class CreateNoteTool : IAgentTool<NoteInput, NoteOutput>
{
    public Task<NoteOutput> ExecuteAsync(NoteInput input, AgentToolContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new NoteOutput(Guid.NewGuid()));
}

[AgentTool("test.publish_note", "Publishes a note.", Risk = AgentToolRisk.SensitiveMutation, Permission = TestPermissions.NotesWrite)]
public sealed class PublishNoteTool : IAgentTool<NoteInput, NoteOutput>
{
    public Task<NoteOutput> ExecuteAsync(NoteInput input, AgentToolContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new NoteOutput(Guid.NewGuid()));
}

[AgentTool("test.send_email", "Sends an email.", Risk = AgentToolRisk.ExternalOrIrreversible, Permission = TestPermissions.MailSend,
    Idempotent = false)]
public sealed class SendEmailTool : IAgentTool<NoteInput, NoteOutput>
{
    public Task<NoteOutput> ExecuteAsync(NoteInput input, AgentToolContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new NoteOutput(Guid.NewGuid()));
}

public static class TestPermissions
{
    public const string NotesRead = "notes.read";
    public const string NotesWrite = "notes.write";
    public const string MailSend = "mail.send";

    public static IReadOnlySet<string> Of(params string[] permissions) => permissions.ToHashSet(StringComparer.Ordinal);
}

public sealed record Summary(string Title, int Items);

public static class TestAgents
{
    public static readonly Type[] AllTools =
        [typeof(EchoTool), typeof(ReadDocumentTool), typeof(CreateNoteTool), typeof(PublishNoteTool), typeof(SendEmailTool)];

    public static AgentDefinition Definition(string key = "test.assistant", AgentAutonomy autonomy = AgentAutonomy.Execute,
        Type? outputType = null, params string[] tools)
    {
        return new AgentDefinition
        {
            Key = key,
            Name = new LocalizedText("Assistant", "مساعد"),
            Instructions = "Help the user.",
            Autonomy = autonomy,
            OutputType = outputType,
            Tools = tools.Length > 0
                ? tools
                : ["test.echo", "test.read_document", "test.create_note", "test.publish_note", "test.send_email"]
        };
    }

    public static AgentRegistry Registry(params AgentDefinition[] agents) =>
        new(AllTools.Select(t => new AgentToolRegistration(t)), agents.Length > 0 ? agents : [Definition()]);
}
