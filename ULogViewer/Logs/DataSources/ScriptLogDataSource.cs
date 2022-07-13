using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CarinaStudio.ULogViewer.Logs.DataSources;

/// <summary>
/// Script-based implementation of <see cref="ILogDataSource"/>.
/// </summary>
class ScriptLogDataSource : BaseLogDataSource
{
    // Constructor.
    internal ScriptLogDataSource(ScriptLogDataSourceProvider provider, LogDataSourceOptions options) : base(provider, options)
    { }


    /// <inheritdoc/>
    protected override void OnReaderClosed()
    {
        base.OnReaderClosed();
    }


    /// <inheritdoc/>
    protected override Task<(LogDataSourceState, TextReader?)> OpenReaderCoreAsync(CancellationToken cancellationToken)
    {
        throw new System.NotImplementedException();
    }


    /// <inheritdoc/>
    protected override Task<LogDataSourceState> PrepareCoreAsync(CancellationToken cancellationToken)
    {
        throw new System.NotImplementedException();
    }
}