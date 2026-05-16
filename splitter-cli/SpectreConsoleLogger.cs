using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace splitter;


/// <summary>
/// Spectre.Console-based live TUI logger.
/// - Title centered at top of outer box
/// - Progress section (N rows) with name, gradient bar, %, ETA, FPS
/// - Log section taking remaining space, auto-scrolling to latest messages
/// - Bottom row with a [ Cancel ] button (dummy handler: key 'c' / 'C')
/// - Resizes with console window
/// </summary>
public sealed class SpectreConsoleLogger : ILogger, IDisposable
{
    private readonly object _sync = new();
    private readonly List<LogEntry> _logs = new();
    private readonly Dictionary<int, ProgressEntry> _progress = new();

    private readonly CancellationTokenSource _cts = new();
    private Task?                            _uiTask;
    private Task?                            _inputTask;
    private int                              _numberOfProcesses = 1;
    private const int                        _maxLogEntries = 500;

    // Public configuration
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Number of logical progress rows. UI reacts dynamically.
    /// </summary>
    public int NumberOfProcesses
    {
        get => _numberOfProcesses;
        set
        {
            lock (_sync)
            {
                _numberOfProcesses = Math.Max(1, value);
                for (int i = 0; i < _numberOfProcesses; i++)
                {
                    if (!_progress.ContainsKey(i))
                        _progress[i] = ProgressEntry.Empty;
                }
            }
        }
    }


    // ---- ILogger ----

    public void ClearProgress(int progressLevel)
    {
        lock (_sync)
        {
            _progress[progressLevel] = ProgressEntry.Empty;
        }
    }

    public void DrawProgress(string name, int progressLine, double progress, TimeSpan eta, double speed)
    {
        lock (_sync)
        {
            if (progressLine < 0)
                return;

            if (progressLine >= NumberOfProcesses)
                NumberOfProcesses = progressLine + 1;

            _progress[progressLine] = new ProgressEntry(
                name ?? string.Empty,
                Math.Clamp(progress, 0.0, 1.0),
                eta,
                speed
            );
        }
    }

    public void Log(string prefix, ConsoleColor color, string msg)
    {
        lock (_sync)
        {
            if (_logs.Count >= _maxLogEntries)
                _logs.RemoveRange(0, _logs.Count - _maxLogEntries + 1);

            _logs.Add(new LogEntry(
                DateTime.Now,
                prefix ?? string.Empty,
                color,
                msg ?? string.Empty
            ));
        }
    }

    // ---- UI lifecycle ----

    /// <summary>
    /// Starts the live TUI loop. This method blocks until the cancellation token is triggered.
    /// </summary>
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (_uiTask != null)
            throw new InvalidOperationException("UI already started.");

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        var token = linkedCts.Token;

        _uiTask = Task.Run(() => RunUiAsync(token), token);
        _inputTask = Task.Run(() => RunInputLoopAsync(token), token);

        return _uiTask;
    }

    private async Task RunUiAsync(CancellationToken token)
    {
        await AnsiConsole.Live(BuildRoot())
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        ctx.UpdateTarget(BuildRoot());
                        await Task.Delay(100, token);
                    }
                    catch ( Exception )
                    {
                        break;
                    }
                }
            });
    }

    private async Task RunInputLoopAsync(CancellationToken token)
    {
        // Dummy handler for Cancel button: press 'c' or 'C'
        while (!token.IsCancellationRequested)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.KeyChar == 'c' || key.KeyChar == 'C')
                {
                    ((ILogger)this).LogWarn("Cancel button pressed (dummy handler).");
                }
            }

            await Task.Delay(50, token).ConfigureAwait(false);
        }
    }

    // ---- Rendering ----

    private IRenderable BuildRoot()
    {
        List<ProgressEntry> progressSnapshot;
        List<LogEntry> logSnapshot;
        string titleSnapshot;
        int numberOfProcessesSnapshot;

        lock (_sync)
        {
            titleSnapshot = Title;
            numberOfProcessesSnapshot = NumberOfProcesses;
            progressSnapshot = Enumerable.Range(0, numberOfProcessesSnapshot)
                .Select(i => _progress.TryGetValue(i, out var p) ? p : ProgressEntry.Empty)
                .ToList();
            logSnapshot = _logs.ToList();
        }

        var layout = new Layout("root")
            .SplitRows(
                new Layout("progress") { Size = Math.Max(3, numberOfProcessesSnapshot + 2) },
                new Layout("log")
                //new Layout("buttons") { Size = 3 }
            );

        layout["progress"].Update(BuildProgressPanel(progressSnapshot));
        layout["log"].Update(BuildLogPanel(logSnapshot));
        //layout["buttons"].Update(BuildButtonsPanel());
        return layout;
    }

    private static IRenderable BuildProgressPanel(IReadOnlyList<ProgressEntry> entries)
    {
        var table = new Table()
            .Expand()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Name[/]")
            .AddColumn("[bold]Progress[/]")
            .AddColumn("[bold]%[/]")
            .AddColumn("[bold]ETA[/]")
            .AddColumn("[bold]FPS[/]");

        foreach (var entry in entries)
        {
            var bar = new DynamicGradientBar(entry.Progress);

            var percentText = $"{entry.Progress * 100:0.0}%";
            var etaText = entry.Eta == TimeSpan.Zero
                ? "--:--"
                : $"{(int)entry.Eta.TotalMinutes:00}:{entry.Eta.Seconds:00}";
            var fpsText = entry.Speed <= 0 ? "-" : $"{entry.Speed:0.0}";

            table.AddRow(
                new Markup(Escape(entry.Name)),
                bar,
                new Markup(percentText),
                new Markup(etaText),
                new Markup(fpsText)
            );
        }
        return table;
    }

    private static IRenderable BuildLogPanel(IReadOnlyList<LogEntry> logs)
    {
        const int maxVisible = 200;
        var slice = logs.Count > maxVisible
        ? logs.Skip(logs.Count - maxVisible).ToList()
        : logs.ToList();

        var rows = new List<IRenderable>();

        foreach (var log in slice)
        {
            var time = log.Timestamp.ToString("HH:mm:ss");
            var timeColor = "deepskyblue1";          // dark-ish blue
            var prefixColor = "lightpink1";          // light magenta
            var msgColor = MapConsoleColor(log.Color);

            var line =
            $"[{timeColor}]{Escape(time)}[/] " +
            $"[{prefixColor}]{Escape(log.Prefix)}[/] " +
            $"[{msgColor}]{Escape(log.Message)}[/]";

            rows.Add(new Markup(line));
        }

        IRenderable content =
        rows.Count == 0
            ? new Markup("[grey]No log messages yet.[/]")
            : new Rows(rows);

        var panel = new Panel(content)
        {
            Header = new PanelHeader("Log", Justify.Left),
            Border = BoxBorder.Rounded,
            Expand = true
        };

        return panel;
    }


    private static IRenderable BuildButtonsPanel()
    {
        // Visual [ Cancel ] button; key handling is in RunInputLoopAsync
        var text = new Markup("[bold white on red] Cancel [/]");
        var grid = new Grid();
        grid.AddColumn(new GridColumn().Centered());
        grid.AddRow(text);

        var panel = new Panel(grid)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader("Actions", Justify.Left),
            Expand = true
        };

        return panel;
    }

    // ---- Helpers ----

    private static string Escape(string value) =>
        value is null ? string.Empty : Markup.Escape(value);

    private static string MapConsoleColor(ConsoleColor color) =>
        color switch
        {
            ConsoleColor.Black => "black",
            ConsoleColor.DarkBlue => "navy",
            ConsoleColor.DarkGreen => "green",
            ConsoleColor.DarkCyan => "teal",
            ConsoleColor.DarkRed => "maroon",
            ConsoleColor.DarkMagenta => "purple",
            ConsoleColor.DarkYellow => "olive",
            ConsoleColor.Gray => "silver",
            ConsoleColor.DarkGray => "grey",
            ConsoleColor.Blue => "blue",
            ConsoleColor.Green => "lime",
            ConsoleColor.Cyan => "aqua",
            ConsoleColor.Red => "red",
            ConsoleColor.Magenta => "fuchsia",
            ConsoleColor.Yellow => "yellow",
            ConsoleColor.White => "white",
            _ => "white"
        };

    /// <summary>
    /// Renders a horizontal gradient bar (blue → yellow → green) for the given progress [0..1].
    /// </summary>
    private static string RenderGradientBar(double progress, int width)
    {
        progress = Math.Clamp(progress, 0.0, 1.0);
        if (width <= 0)
            return string.Empty;

        int filled = (int)Math.Round(progress * width);
        int empty = width - filled;

        if (filled <= 0)
            return $"[grey]{new string('─', width)}[/]";

        // Split filled part into three segments: blue / yellow / green
        // low progress: mostly blue; mid: yellow; high: green
        int blueCount = (int)Math.Round(filled * 0.33);
        int yellowCount = (int)Math.Round(filled * 0.34);
        int greenCount = filled - blueCount - yellowCount;

        var sb = new StringBuilder();

        if (blueCount > 0)
        {
            sb.Append("[blue]");
            sb.Append(new string('█', blueCount));
            sb.Append("[/]");
        }

        if (yellowCount > 0)
        {
            sb.Append("[yellow]");
            sb.Append(new string('█', yellowCount));
            sb.Append("[/]");
        }

        if (greenCount > 0)
        {
            sb.Append("[green]");
            sb.Append(new string('█', greenCount));
            sb.Append("[/]");
        }

        if (empty > 0)
        {
            sb.Append("[grey]");
            sb.Append(new string('─', empty));
            sb.Append("[/]");
        }

        return sb.ToString();
    }

    // ---- Types & disposal ----

    private readonly record struct ProgressEntry(
        string Name,
        double Progress,
        TimeSpan Eta,
        double Speed)
    {
        public static ProgressEntry Empty => new(string.Empty, 0.0, TimeSpan.Zero, 0.0);
    }

    private readonly record struct LogEntry(
        DateTime Timestamp,
        string Prefix,
        ConsoleColor Color,
        string Message);

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _uiTask?.Wait(500);
            _inputTask?.Wait(500);
        }
        catch
        {
            // ignore
        }
        _cts.Dispose();
    }

    private sealed class DynamicGradientBar : IRenderable
    {
        private readonly double _progress;

        public DynamicGradientBar(double progress)
        {
            _progress = Math.Clamp(progress, 0, 1);
        }

        public Measurement Measure(RenderOptions options, int maxWidth)
        {
            // Use the full width Spectre gives us
            var width = Math.Max(1, maxWidth);
            return new Measurement(width, width);
        }

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            var width = Math.Max(1, maxWidth);

            // Your gradient bar string WITH markup
            var bar = RenderGradientBar(_progress, width);

            // Wrap it in a Markup renderable
            var markup = new Markup(bar);

            // Correct: delegate rendering to Markup
            foreach (var segment in ((IRenderable)markup).Render(options, maxWidth))
                yield return segment;
        }
    }

}
