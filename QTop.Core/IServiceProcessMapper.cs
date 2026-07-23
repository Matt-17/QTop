namespace QTop.Core;

public interface IServiceProcessMapper
{
    IReadOnlyDictionary<int, IReadOnlyList<string>> GetServiceNamesByProcessId();
}
