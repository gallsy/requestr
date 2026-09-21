namespace Requestr.Core.Models;

public enum FormConditionOperator
{
    Equals = 0,
    NotEquals = 1,
    IsEmpty = 2,
    IsNotEmpty = 3
}

public sealed class FormCondition
{
    public string Field { get; set; } = "";
    public FormConditionOperator Operator { get; set; }
    public string? Value { get; set; }
}