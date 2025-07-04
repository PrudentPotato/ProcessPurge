# ProcessPurge v0.9.1

![ProcessPurge Logo](logo.png)

Do you have a bunch of background apps you usually want to load, but frequently want to terminate to free up system resources? ProcessPurge is a lightweight and powerful Windows utility designed to help you quickly terminate unnecessary applications and free up system resources.

## Purpose

ProcessPurge allows users to create a pre-defined, ordered list of applications to close. With a single click from the system tray, you can "purge" these selected processes, making it ideal for gamers, developers, or power users who need to quickly optimize their system's performance without navigating the Task Manager.

## Key Features

* **Comprehensive Process List:** View all currently running processes on your system.

* **Advanced Sorting:** Sort the process list by name, memory usage, total CPU time, or selection status in both ascending and descending order.

* **Customizable Purge List:** Select specific processes to add to a "Termination Order" list.

* **Ordered Termination:** Arrange the processes in your purge list in the exact order you want them to be terminated.

* **System Tray Integration:** The application runs quietly in the system tray for quick access. Right-click the icon to instantly purge processes, open the main window, access settings, or exit.

* **Flexible Termination Options:**

  * **Polite Kill (Default):** Attempts to close applications gracefully first, only force-killing them if they don't respond.

  * **Force Kill:** Immediately terminates processes.

* **Safety Features:**

  * **Critical Process Protection (Default):** Prevents you from accidentally selecting and terminating essential Windows system processes. This can be disabled by advanced users in the settings.

  * **Confirmation Dialog:** A confirmation prompt appears before purging from the main window to prevent accidental clicks.

* **Persistent Settings:** Your custom purge list and all your settings are automatically saved and reloaded every time you start the application.

* **Automatic Startup:** Configure ProcessPurge to start automatically and silently with Windows via the Task Scheduler, ensuring it's ready when you need it without any UAC prompts on login.

## Requirements

* **Operating System:** Windows 10 or Windows 11.

* **Framework:** .NET 8 Desktop Runtime (or newer). The installer should handle this dependency.

* **Permissions:** The application requires administrator privileges to terminate processes and manage the startup task. It will request these permissions upon launch.

## Installation

1. Run the provided `setup.exe` installer.

2. Follow the on-screen instructions. The installer will create the necessary shortcuts and the uninstaller.

3. On the first launch, you will be asked to accept the user agreement.

## How to Use

1. **Select Processes:** On the "Process Selection" tab, check the box next to any process you want to add to your purge list.

2. **Order Your List:** Go to the "Termination Order" tab. Select a process and use the "Move Up," "Move Down," and "Remove" buttons to arrange the list to your liking. The list is terminated from top to bottom.

3. **Purge:**

   * **From the Main Window:** Click the "Purge Selected" button for a safe, confirmed purge.

   * **From the System Tray:** Right-click the ProcessPurge icon and select "Purge" for a fast, unconfirmed purge.

4. **Configure Settings:** Right-click the tray icon and select "Settings" to configure startup behavior, termination methods, and safety features.

## Disclaimer

You are using this software solely at your own risk. It is provided 'as is' without warranty of any kind. The author is not responsible for any data loss or system instability that may result from its use.

With incredible help from Google Gemini Pro 2.5 and Grok.

*Developed by David Rader II*
*https://chexed.net/*
