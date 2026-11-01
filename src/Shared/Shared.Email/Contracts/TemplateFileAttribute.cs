namespace Shared.Email.Contracts;

/// <summary>
/// Decorates an <see cref="EmailTemplate"/> enum value with the name of its Liquid template file
/// (without the .liquid extension). Used by <see cref="Services.FluidTemplateRenderer"/> to
/// resolve enum values to template names and validate coverage at startup.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class TemplateFileAttribute(string fileName) : Attribute
{
    public string FileName { get; } = fileName;
}
