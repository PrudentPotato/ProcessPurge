// MainWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection; // Required for getting the version
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using Microsoft.Win32.TaskScheduler;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;
using Task = System.Threading.Tasks.Task;

namespace ProcessPurge
{
    /// <summary>
    /// Represents a single process with its relevant information.
    /// </summary>
    public class ProcessInfo
    {
        public string? Name { get; set; }
        public long Memory { get; set; }
        public bool IsSelected { get; set; }
        public float CpuUsage { get; set; }
        public TimeSpan ProcessorTime { get; set; } // Re-added for the fast method
        public bool IsCritical { get; set; }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // --- Member Variables ---
        private GridViewColumnHeader? _lastHeaderClicked = null;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;
        private readonly NotifyIcon? _notifyIcon;
        private AppSettings _settings = new();
        private readonly string _settingsFilePath;

        private readonly HashSet<string> _criticalProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "svchost", "csrss", "wininit", "winlogon", "lsass", "smss", "services"
        };

        public ObservableCollection<ProcessInfo> PurgeList { get; set; } = new();
        private List<ProcessInfo> AllProcesses { get; set; } = new();

        public string WindowTitle { get; }

        public MainWindow()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            WindowTitle = $"ProcessPurge v{version?.Major}.{version?.Minor}.{version?.Build}";
            this.DataContext = this;

            _settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProcessPurge", "settings.json");
            LoadSettings();

            if (!_settings.EulaAccepted)
            {
                var eula = new EulaWindow();
                if (eula.ShowDialog() == true)
                {
                    _settings.EulaAccepted = true;
                    SaveSettings();
                }
                else
                {
                    System.Windows.Application.Current.Shutdown();
                    return;
                }
            }

            InitializeComponent();
            PurgeListView.ItemsSource = PurgeList;

            // Set initial column visibility based on settings
            UpdateColumnVisibility();

            _ = InitialLoadAndRefresh();

            ManageStartupTask();

            _notifyIcon = new NotifyIcon
            {
                Icon = new Icon("logo.ico"),
                Visible = true,
                Text = "ProcessPurge"
            };
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Purge", null, (s, e) => PurgeProcesses(false));
            contextMenu.Items.Add("Open", null, (s, e) => ShowWindow());
            contextMenu.Items.Add("Settings", null, (s, e) => OpenSettings());
            contextMenu.Items.Add("About", null, (s, e) => ShowAbout());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());
            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => ShowWindow();
        }

        private async Task InitialLoadAndRefresh()
        {
            await LoadProcessList();
            await Task.Delay(1500);
            await LoadProcessList();
        }

        /// <summary>
        /// Shows or hides the CPU columns based on the current setting by changing their width.
        /// </summary>
        private void UpdateColumnVisibility()
        {
            if (_settings.ShowCpuPercentage)
            {
                // Show the CPU % column by setting its width from 0 to its original value.
                CpuPercentageColumn.Width = 100;
                // Hide the CPU Time column by setting its width to 0.
                CpuTimeColumn.Width = 0;
            }
            else
            {
                // Hide the CPU % column.
                CpuPercentageColumn.Width = 0;
                // Show the CPU Time column.
                CpuTimeColumn.Width = 150;
            }
        }

        private async Task LoadProcessList()
        {
            var processes = Process.GetProcesses();
            var processInfos = new List<ProcessInfo>();

            // --- Conditional CPU Calculation ---
            if (_settings.ShowCpuPercentage)
            {
                // SLOW METHOD: Calculate live CPU percentage
                var cpuCounters = new Dictionary<int, PerformanceCounter>();
                var category = new PerformanceCounterCategory("Process");
                var instanceNames = category.GetInstanceNames();
                var processIdToInstanceName = new Dictionary<int, string>();

                foreach (var instanceName in instanceNames)
                {
                    using (var counter = new PerformanceCounter("Process", "ID Process", instanceName, readOnly: true))
                    {
                        try
                        {
                            if (!processIdToInstanceName.ContainsKey((int)counter.RawValue))
                                processIdToInstanceName.Add((int)counter.RawValue, instanceName);
                        }
                        catch { }
                    }
                }

                foreach (var process in processes)
                {
                    if (processIdToInstanceName.TryGetValue(process.Id, out var instanceName))
                    {
                        try
                        {
                            var counter = new PerformanceCounter("Process", "% Processor Time", instanceName, readOnly: true);
                            counter.NextValue();
                            cpuCounters.Add(process.Id, counter);
                        }
                        catch { }
                    }
                }

                await Task.Delay(1000);

                foreach (var process in processes)
                {
                    float cpuUsage = 0;
                    if (cpuCounters.TryGetValue(process.Id, out var counter))
                    {
                        try { cpuUsage = counter.NextValue() / Environment.ProcessorCount; } catch { }
                    }
                    processInfos.Add(CreateProcessInfo(process, cpuUsage: cpuUsage));
                }

                foreach (var counter in cpuCounters.Values) counter.Dispose();
            }
            else
            {
                // FAST METHOD: Get total CPU time only
                foreach (var process in processes)
                {
                    processInfos.Add(CreateProcessInfo(process));
                }
            }

            AllProcesses = processInfos.OrderBy(p => p.Name).ToList();
            ProcessListView.ItemsSource = AllProcesses;
            CollectionViewSource.GetDefaultView(ProcessListView.ItemsSource)?.Refresh();
        }

        /// <summary>
        /// Helper method to create a ProcessInfo object.
        /// </summary>
        private ProcessInfo CreateProcessInfo(Process process, float cpuUsage = 0)
        {
            TimeSpan processorTime = TimeSpan.Zero;
            try { processorTime = process.TotalProcessorTime; } catch { }

            return new ProcessInfo
            {
                Name = process.ProcessName,
                Memory = process.WorkingSet64 / (1024 * 1024),
                CpuUsage = cpuUsage,
                ProcessorTime = processorTime,
                IsCritical = _settings.BlockCriticalProcesses && _criticalProcesses.Contains(process.ProcessName),
                IsSelected = PurgeList.Any(p => p.Name == process.ProcessName)
            };
        }

        private async void PurgeProcesses(bool showConfirmation)
        {
            if (PurgeList.Count == 0) return;

            if (showConfirmation)
            {
                var result = MessageBox.Show($"Are you sure you want to terminate {PurgeList.Count} process(es)?", "Confirm Purge", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No) return;
            }

            int terminatedCount = 0;
            var selfDestruct = PurgeList.FirstOrDefault(p => p.Name?.Equals("ProcessPurge", StringComparison.OrdinalIgnoreCase) ?? false);

            foreach (var processInfo in PurgeList.ToList())
            {
                if (processInfo == selfDestruct || string.IsNullOrEmpty(processInfo.Name)) continue;
                try
                {
                    var processesToKill = Process.GetProcessesByName(processInfo.Name);
                    foreach (var p in processesToKill)
                    {
                        if (_settings.PoliteKill)
                        {
                            if (p.CloseMainWindow())
                            {
                                await Task.Delay(2000);
                                if (!p.HasExited) p.Kill();
                            }
                            else { p.Kill(); }
                        }
                        else { p.Kill(); }
                        terminatedCount++;
                    }
                }
                catch { }
            }

            if (selfDestruct != null)
            {
                ExitApplication();
            }
            else
            {
                MessageBox.Show($"{terminatedCount} process(es) terminated.", "Purge Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            await LoadProcessList();
        }

        #region Window and UI Events
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveSettings();
            if (_settings.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                this.Visibility = Visibility.Hidden;
            }
            else
            {
                ExitApplication();
            }
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is ProcessInfo processInfo)
            {
                if (checkBox.IsChecked == true)
                {
                    if (!PurgeList.Any(p => p.Name == processInfo.Name))
                    {
                        PurgeList.Add(processInfo);
                    }
                }
                else
                {
                    var itemToRemove = PurgeList.FirstOrDefault(p => p.Name == processInfo.Name);
                    if (itemToRemove != null) PurgeList.Remove(itemToRemove);
                }

                var self = PurgeList.FirstOrDefault(p => p.Name?.Equals("ProcessPurge", StringComparison.OrdinalIgnoreCase) ?? false);
                if (self != null)
                {
                    PurgeList.Move(PurgeList.IndexOf(self), PurgeList.Count - 1);
                }
            }
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (PurgeListView.SelectedItem is ProcessInfo selectedItem)
            {
                if (selectedItem.Name?.Equals("ProcessPurge", StringComparison.OrdinalIgnoreCase) ?? false) return;
                int currentIndex = PurgeList.IndexOf(selectedItem);
                if (currentIndex > 0)
                {
                    PurgeList.Move(currentIndex, currentIndex - 1);
                }
            }
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (PurgeListView.SelectedItem is ProcessInfo selectedItem)
            {
                if (selectedItem.Name?.Equals("ProcessPurge", StringComparison.OrdinalIgnoreCase) ?? false) return;
                int currentIndex = PurgeList.IndexOf(selectedItem);
                int limit = PurgeList.Any(p => p.Name?.Equals("ProcessPurge", StringComparison.OrdinalIgnoreCase) ?? false) ? PurgeList.Count - 2 : PurgeList.Count - 1;
                if (currentIndex < limit && currentIndex != -1)
                {
                    PurgeList.Move(currentIndex, currentIndex + 1);
                }
            }
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (PurgeListView.SelectedItem is ProcessInfo selectedItem)
            {
                var mainListItem = AllProcesses.FirstOrDefault(p => p.Name == selectedItem.Name);
                if (mainListItem != null)
                {
                    mainListItem.IsSelected = false;
                }
                PurgeList.Remove(selectedItem);
                CollectionViewSource.GetDefaultView(ProcessListView.ItemsSource)?.Refresh();
            }
        }

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is GridViewColumnHeader headerClicked && headerClicked.Tag != null)
            {
                ListSortDirection direction = (_lastHeaderClicked == headerClicked && _lastDirection == ListSortDirection.Ascending) ?
                                              ListSortDirection.Descending : ListSortDirection.Ascending;
                string sortBy = headerClicked.Tag.ToString()!;
                var dataView = CollectionViewSource.GetDefaultView(ProcessListView.ItemsSource);
                if (dataView != null)
                {
                    dataView.SortDescriptions.Clear();
                    dataView.SortDescriptions.Add(new SortDescription(sortBy, direction));
                }
                _lastHeaderClicked = headerClicked;
                _lastDirection = direction;
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadProcessList();
        private void PurgeButton_Click(object sender, RoutedEventArgs e) => PurgeProcesses(true);
        private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

        private void ChexedLogo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://www.chexed.net/") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open link: {ex.Message}");
            }
        }
        #endregion

        #region System Tray and Settings
        private void ShowWindow()
        {
            this.Visibility = Visibility.Visible;
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void ExitApplication()
        {
            if (_notifyIcon != null) _notifyIcon.Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        private async void OpenSettings()
        {
            var settingsWindow = new SettingsWindow(_settings);
            if (settingsWindow.ShowDialog() == true)
            {
                _settings.StartWithWindows = settingsWindow.StartWithWindowsCheck.IsChecked ?? true;
                _settings.MinimizeToTrayOnClose = settingsWindow.MinimizeToTrayCheck.IsChecked ?? true;
                _settings.PoliteKill = settingsWindow.PoliteKillCheck.IsChecked ?? true;
                _settings.BlockCriticalProcesses = settingsWindow.BlockCriticalCheck.IsChecked ?? true;
                _settings.ShowCpuPercentage = settingsWindow.ShowCpuPercentageCheck.IsChecked ?? false;
                SaveSettings();
                ManageStartupTask();
                UpdateColumnVisibility();
                await LoadProcessList();
            }
        }

        private void ShowAbout()
        {
            var aboutWindow = new AboutWindow();
            aboutWindow.ShowDialog();
        }

        private void LoadSettings()
        {
            try
            {
                var settingsDir = Path.GetDirectoryName(_settingsFilePath);
                if (settingsDir != null && !Directory.Exists(settingsDir))
                {
                    Directory.CreateDirectory(settingsDir);
                }

                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

                    foreach (var processName in _settings.PurgeList)
                    {
                        if (!string.IsNullOrEmpty(processName))
                        {
                            PurgeList.Add(new ProcessInfo { Name = processName, IsSelected = true });
                        }
                    }
                }
            }
            catch { _settings = new AppSettings(); }
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
                _settings.PurgeList = PurgeList.Select(p => p.Name!).ToList();
                string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save settings: " + ex.Message);
            }
        }

        private void ManageStartupTask()
        {
            const string taskName = "ProcessPurgeStartup";
            try
            {
                using (var ts = new TaskService())
                {
                    var existingTask = ts.GetTask(taskName);
                    if (_settings.StartWithWindows)
                    {
                        if (existingTask == null)
                        {
                            var td = ts.NewTask();
                            td.RegistrationInfo.Description = "Starts ProcessPurge at login.";
                            td.Principal.RunLevel = TaskRunLevel.Highest;
                            td.Triggers.Add(new LogonTrigger());
                            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                            if (exePath != null)
                            {
                                td.Actions.Add(new ExecAction(exePath));
                                ts.RootFolder.RegisterTaskDefinition(taskName, td);
                            }
                        }
                    }
                    else
                    {
                        if (existingTask != null)
                        {
                            ts.RootFolder.DeleteTask(taskName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not manage startup task. Please run as administrator.\nError: {ex.Message}", "Task Scheduler Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion
    }
}
