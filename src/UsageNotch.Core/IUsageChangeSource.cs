namespace UsageNotch.Core;

public interface IUsageChangeSource
{
    event EventHandler? Changed;
}
