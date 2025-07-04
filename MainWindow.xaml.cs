// MainWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
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
        public TimeSpan ProcessorTime { get; set; }
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

        // List of critical system processes to protect from termination.
        private readonly HashSet<string> _criticalProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "svchost", "csrss", "wininit", "winlogon", "lsass", "smss", "services"
        };

        // Data collections that power the UI lists.
        public ObservableCollection<ProcessInfo> PurgeList { get; set; } = new();
        private List<ProcessInfo> AllProcesses { get; set; } = new();

        /// <summary>
        /// Constructor: Runs once when the application starts.
        /// </summary>
        public MainWindow()
        {
            // Define where the settings file will be saved.
            _settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProcessPurge", "settings.json");
            LoadSettings();

            // Show the End-User License Agreement on first run.
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

            // Initialize the UI components defined in XAML.
            InitializeComponent();

            // Link the UI list to our data collection.
            PurgeListView.ItemsSource = PurgeList;

            // Populate the main process list.
            LoadProcessList();

            // --- System Tray Icon Setup ---
            _notifyIcon = new NotifyIcon
            {
                Icon = new Icon("logo.ico"),
                Visible = true,
                Text = "ProcessPurge"
            };
            // Create the right-click menu for the tray icon.
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Purge", null, (s, e) => PurgeProcesses(false));
            contextMenu.Items.Add("Open", null, (s, e) => ShowWindow());
            contextMenu.Items.Add("Settings", null, (s, e) => OpenSettings());
            contextMenu.Items.Add("About", null, (s, e) => ShowAbout());
            contextMenu.Items.Add("-"); // A separator line.
            contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());
            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => ShowWindow();
        }

        /// <summary>
        /// Scans the system for all running processes and populates the main list.
        /// </summary>
        private void LoadProcessList()
        {
            UpdatePurgeListFromCheckboxes();

            AllProcesses.Clear();
            var processes = Process.GetProcesses().OrderBy(p => p.ProcessName).ToArray();

            foreach (var process in processes)
            {
                TimeSpan processorTime = TimeSpan.Zero;
                try { processorTime = process.TotalProcessorTime; } catch { /* Ignore access errors */ }

                var processInfo = new ProcessInfo
                {
                    Name = process.ProcessName,
                    Memory = process.WorkingSet64 / 1024,
                    ProcessorTime = processorTime,
                    IsCritical = _settings.BlockCriticalProcesses && _criticalProcesses.Contains(process.ProcessName),
                    IsSelected = PurgeList.Any(p => p.Name == process.ProcessName)
                };
                AllProcesses.Add(processInfo);
            }
            ProcessListView.ItemsSource = AllProcesses;
            CollectionViewSource.GetDefaultView(ProcessListView.ItemsSource)?.Refresh();
        }

        /// <summary>
        /// Terminates all processes in the PurgeList.
        /// </summary>
        private async void PurgeProcesses(bool showConfirmation)
        {
            if (PurgeList.Count == 0) return;

            // Optionally show a confirmation dialog.
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
                        // Use polite or forceful termination based on settings.
                        if (_settings.PoliteKill)
                        {
                            if (p.CloseMainWindow())
                            {
                                await Task.Delay(2000); // Wait 2 seconds for it to close.
                                if (!p.HasExited) p.Kill(); // Force kill if it's still running.
                            }
                            else { p.Kill(); }
                        }
                        else { p.Kill(); }
                        terminatedCount++;
                    }
                }
                catch { /* Ignore errors if a process can't be killed */ }
            }

            // Handle self-termination last.
            if (selfDestruct != null)
            {
                ExitApplication();
            }
            else
            {
                MessageBox.Show($"{terminatedCount} process(es) terminated.", "Purge Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            LoadProcessList();
        }

        #region Window and UI Events
        /// <summary>
        /// Handles the main window's closing event.
        /// </summary>
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            UpdatePurgeListFromCheckboxes();
            SaveSettings();
            // Hide to tray or exit completely based on settings.
            if (_settings.MinimizeToTrayOnClose)
            {
                e.Cancel = true; // Prevent the window from closing.
                this.Visibility = Visibility.Hidden;
            }
            else
            {
                ExitApplication();
            }
        }

        /// <summary>
        /// Updates the PurgeList based on which boxes are checked in the main list.
        /// </summary>
        private void UpdatePurgeListFromCheckboxes()
        {
            var selectedProcesses = AllProcesses.Where(p => p.IsSelected).ToList();
            var currentOrder = PurgeList.ToList();
            PurgeList.Clear();

            // Re-add items in their previously saved order.
            foreach (var orderedItem in currentOrder)
            {
                var matchingSelectedItem = selectedProcesses.FirstOrDefault(p => p.Name == orderedItem.Name);
                if (matchingSelectedItem != null)
                {
                    PurgeList.Add(matchingSelectedItem);
                    selectedProcesses.Remove(matchingSelectedItem);
                }
            }
            // Add any newly selected items.
            foreach (var newItem in selectedProcesses)
            {
                PurgeList.Add(newItem);
            }

            // Ensure ProcessPurge is always last if selected.
            var self = PurgeList.FirstOrDefault(p => p.Name?.Equals("ProcessPurge", StringComparison.OrdinalIgnoreCase) ?? false);
            if (self != null)
            {
                PurgeList.Move(PurgeList.IndexOf(self), PurgeList.Count - 1);
            }
        }

        /// <summary>
        /// Fires whenever any checkbox in the main list is clicked.
        /// </summary>
        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdatePurgeListFromCheckboxes();
        }

        /// <summary>
        /// Moves the selected item up in the Termination Order list.
        /// </summary>
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

        /// <summary>
        /// Moves the selected item down in the Termination Order list.
        /// </summary>
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

        /// <summary>
        /// Removes the selected item from the Termination Order list.
        /// </summary>
        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (PurgeListView.SelectedItem is ProcessInfo selectedItem)
            {
                // Uncheck it in the main list.
                var mainListItem = AllProcesses.FirstOrDefault(p => p.Name == selectedItem.Name);
                if (mainListItem != null)
                {
                    mainListItem.IsSelected = false;
                }

                // Remove it from the purge list.
                PurgeList.Remove(selectedItem);

                // Refresh the main list's UI to show the unchecked box.
                CollectionViewSource.GetDefaultView(ProcessListView.ItemsSource)?.Refresh();
            }
        }

        /// <summary>
        /// Handles sorting when a column header is clicked.
        /// </summary>
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

        // --- Main Action Button Clicks ---
        private void PurgeButton_Click(object sender, RoutedEventArgs e) => PurgeProcesses(true);
        private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadProcessList();
        private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

        /// <summary>
        /// Opens the default web browser to the specified URL.
        /// </summary>
        private void ChexedLogo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // UseShellExecute is important for opening URLs in the default browser.
                Process.Start(new ProcessStartInfo("https://www.chexed.net/") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open link: {ex.Message}");
            }
        }
        #endregion

        #region System Tray and Settings
        /// <summary>
        /// Shows the main window when it's hidden.
        /// </summary>
        private void ShowWindow()
        {
            this.Visibility = Visibility.Visible;
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        /// <summary>
        /// Properly disposes the tray icon and shuts down the application.
        /// </summary>
        private void ExitApplication()
        {
            if (_notifyIcon != null) _notifyIcon.Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        /// <summary>
        /// Opens the settings window and applies changes if saved.
        /// </summary>
        private void OpenSettings()
        {
            var settingsWindow = new SettingsWindow(_settings);
            if (settingsWindow.ShowDialog() == true)
            {
                _settings.StartWithWindows = settingsWindow.StartWithWindowsCheck.IsChecked ?? true;
                _settings.MinimizeToTrayOnClose = settingsWindow.MinimizeToTrayCheck.IsChecked ?? true;
                _settings.PoliteKill = settingsWindow.PoliteKillCheck.IsChecked ?? true;
                _settings.BlockCriticalProcesses = settingsWindow.BlockCriticalCheck.IsChecked ?? true;
                SaveSettings();
                ManageStartupTask();
                LoadProcessList(); // Refresh list to apply critical process block.
            }
        }

        /// <summary>
        /// Shows the About message box.
        /// </summary>
        private void ShowAbout()
        {
            MessageBox.Show(
                "ProcessPurge v0.9.1, July 4, 2025\n" +
                "Sincerely, David Rader II\n\n" +
                "https://chexed.net/\n\n" +
                "Disclaimer: Use at your own risk.",
                "About ProcessPurge");
        }

        /// <summary>
        /// Loads settings from the JSON file.
        /// </summary>
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

                    // After loading settings, populate the PurgeList from the saved order.
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

        /// <summary>
        /// Saves the current settings to a JSON file.
        /// </summary>
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

        /// <summary>
        /// Creates or deletes the scheduled task for starting with Windows.
        /// </summary>
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
                            td.Principal.RunLevel = TaskRunLevel.Highest; // Run as admin without UAC prompt.
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
