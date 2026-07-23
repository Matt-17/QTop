using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using QTop.Core;

namespace QTop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IProcessTerminator _processTerminator = new WindowsProcessTerminator();
    private readonly DispatcherTimer _refreshTimer;
    private readonly AppSettings _settings = AppSettings.Load();

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel(new WindowsProcessProvider())
        {
            ConfirmBeforeKill = _settings.ConfirmBeforeKill,
            HideProtectedSystem = _settings.HideProtectedSystem
        };
        CategoryFilterOption? savedFilter = _viewModel.CategoryFilters
            .FirstOrDefault(option => option.Category?.ToString() == _settings.SelectedCategory);
        if (savedFilter is not null)
            _viewModel.SelectedCategoryFilter = savedFilter;

        DataContext = _viewModel;

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(_viewModel.SelectedRefreshInterval.Seconds)
        };
        _refreshTimer.Tick += RefreshTimer_Tick;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        ProcessGrid.Columns[0].SortDirection = ListSortDirection.Ascending;
        Deactivated += (_, _) => DetailExpandPopup.IsOpen = false;
    }

    private void DetailTextBox_MouseEnter(object sender, MouseEventArgs e)
    {
        // Only expand boxes whose text is actually cut off.
        if (sender is not TextBox box || box.ExtentWidth <= box.ViewportWidth + 1)
            return;

        DetailExpandPopup.IsOpen = false;
        DetailExpandTextBox.Text = box.Text;
        DetailExpandTextBox.MinWidth = box.ActualWidth;
        DetailExpandPopup.PlacementTarget = box;
        DetailExpandPopup.IsOpen = true;
    }

    private void DetailTextBox_MouseLeave(object sender, MouseEventArgs e)
    {
        // Deferred: when the pointer moves onto the overlay itself, keep it open.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!DetailExpandTextBox.IsMouseOver)
                DetailExpandPopup.IsOpen = false;
        });
    }

    private void DetailExpandTextBox_MouseLeave(object sender, MouseEventArgs e)
    {
        DetailExpandPopup.IsOpen = false;
    }

    private void ProcessGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;

        // Metric columns start descending (biggest consumers first), text columns ascending.
        bool descendingFirst = e.Column.SortMemberPath is "CpuPercent" or "CpuTime" or "Memory" or "NameGroupCount";
        ListSortDirection direction = e.Column.SortDirection switch
        {
            ListSortDirection.Ascending => ListSortDirection.Descending,
            ListSortDirection.Descending => ListSortDirection.Ascending,
            _ => descendingFirst ? ListSortDirection.Descending : ListSortDirection.Ascending
        };

        foreach (DataGridColumn column in ProcessGrid.Columns)
            column.SortDirection = null;

        e.Column.SortDirection = direction;
        _viewModel.SetSort(e.Column.SortMemberPath, direction == ListSortDirection.Descending);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        FocusSearchBox();
        await RefreshAsync();
        _refreshTimer.Start();
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == Key.F3 || (control && e.Key == Key.F))
        {
            e.Handled = true;
            FocusSearchBox();
            return;
        }

        if (control && e.Key == Key.Delete)
        {
            e.Handled = true;
            await KillSelectedProcessAsync();
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || SearchBox.Text.Length == 0)
            return;

        e.Handled = true;
        ClearSearch();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        ClearSearch();
        FocusSearchBox();
    }

    private void ClearSearch()
    {
        // Reset the TextBox first so a pending delayed binding update cannot re-apply stale text.
        SearchBox.Text = string.Empty;
        _viewModel.SearchText = string.Empty;
    }

    private void FocusSearchBox()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _refreshTimer.Stop();
        ProcessIconCache.Clear();
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshAsync();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedRefreshInterval))
            ApplyRefreshInterval();

        if (e.PropertyName is nameof(MainWindowViewModel.ConfirmBeforeKill)
            or nameof(MainWindowViewModel.HideProtectedSystem)
            or nameof(MainWindowViewModel.SelectedCategoryFilter))
        {
            _settings.ConfirmBeforeKill = _viewModel.ConfirmBeforeKill;
            _settings.HideProtectedSystem = _viewModel.HideProtectedSystem;
            _settings.SelectedCategory = _viewModel.SelectedCategoryFilter.Category?.ToString();
            _settings.Save();
        }
    }

    private void RefreshIntervalCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ApplyRefreshInterval();
    }

    private async void RefreshNow_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async void RefreshSelectedProcess_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync(_viewModel.SelectedProcess?.ProcessId);
    }

    private void CopyProcessName_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(_viewModel.SelectedProcess?.ProcessName, "process name");
    }

    private void CopyPid_Click(object sender, RoutedEventArgs e)
    {
        string? pid = _viewModel.SelectedProcess?.ProcessId.ToString(CultureInfo.InvariantCulture);
        CopyToClipboard(pid, "PID");
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(_viewModel.SelectedProcess?.Snapshot.ExecutablePath, "executable path");
    }

    private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        string? executablePath = _viewModel.SelectedProcess?.Snapshot.ExecutablePath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            _viewModel.SetAlert("Executable path is unavailable for the selected process.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{executablePath}\"",
                UseShellExecute = true
            });
            _viewModel.SetStatus($"Opened location for {Path.GetFileName(executablePath)}.");
        }
        catch (Exception exception)
        {
            _viewModel.SetAlert($"Open location failed: {exception.Message}");
        }
    }

    private async void KillProcess_Click(object sender, RoutedEventArgs e)
    {
        await KillSelectedProcessAsync();
    }

    private async void ProcessGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
            return;

        e.Handled = true;
        await KillSelectedProcessAsync();
    }

    private async Task KillSelectedProcessAsync()
    {
        ProcessRowViewModel? selected = _viewModel.SelectedProcess;
        if (selected is null)
        {
            _viewModel.SetAlert("No process is selected.");
            return;
        }

        ProcessSnapshot snapshot = selected.Snapshot;
        if (!ProcessActionRules.CanKill(snapshot, Environment.ProcessId))
        {
            _viewModel.SetAlert("QTop cannot kill its own process.");
            return;
        }

        if (_viewModel.ConfirmBeforeKill)
        {
            string executablePath = snapshot.ExecutablePath ?? "Unavailable";
            string confirmation = $"Name: {snapshot.ProcessName}\nPID: {snapshot.ProcessId}\nCategory: {snapshot.Category.ToDisplayName()}\nPath: {executablePath}\n\nKill this process? Child processes will be terminated as well.";
            MessageBoxResult confirmationResult = MessageBox.Show(
                this,
                confirmation,
                "Kill Process",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmationResult != MessageBoxResult.Yes)
            {
                _viewModel.SetStatus("Kill cancelled.");
                return;
            }
        }

        DateTimeOffset? expectedStartTime = snapshot.StartTime;
        ProcessTerminationResult result;
        if (ProcessActionRules.ShouldTryGracefulClose(snapshot))
        {
            result = _processTerminator.TryCloseMainWindow(snapshot.ProcessId, TimeSpan.FromSeconds(2), expectedStartTime);
            if (result.Outcome is ProcessTerminationOutcome.Succeeded or ProcessTerminationOutcome.NotRunning)
            {
                ReportTermination(result);
                await RefreshAsync();
                return;
            }

            if (result.Outcome == ProcessTerminationOutcome.AccessDenied)
            {
                ReportTermination(result);
                return;
            }

            if (_viewModel.ConfirmBeforeKill)
            {
                MessageBoxResult forceResult = MessageBox.Show(
                    this,
                    $"{result.Message}\n\nForce kill {snapshot.ProcessName} ({snapshot.ProcessId})?",
                    "Force Kill Process",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (forceResult != MessageBoxResult.Yes)
                {
                    _viewModel.SetStatus(result.Message);
                    return;
                }
            }
        }

        result = _processTerminator.ForceKill(snapshot.ProcessId, expectedStartTime);
        ReportTermination(result);
        await RefreshAsync();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new AboutWindow
        {
            Owner = this
        };
        window.ShowDialog();
    }


    private async Task RefreshAsync(int? selectedProcessId = null)
    {
        await _viewModel.RefreshAsync(selectedProcessId);
        ApplyRefreshInterval();
    }

    private void ApplyRefreshInterval()
    {
        if (_viewModel.SelectedRefreshInterval is null)
            return;

        _refreshTimer.Interval = TimeSpan.FromSeconds(_viewModel.SelectedRefreshInterval.Seconds);
    }

    private void CopyToClipboard(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "Unavailable")
        {
            _viewModel.SetAlert($"No {label} is available for the selected process.");
            return;
        }

        try
        {
            Clipboard.SetText(value);
            _viewModel.SetStatus($"Copied {label}.");
        }
        catch (ExternalException exception)
        {
            _viewModel.SetAlert($"Clipboard failed: {exception.Message}");
        }
    }

    private void ReportTermination(ProcessTerminationResult result)
    {
        if (result.IsSuccess)
            _viewModel.SetStatus(result.Message);
        else
            _viewModel.SetAlert(result.Message);
    }
}
