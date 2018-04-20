﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace x360ce.App.ViGEm
{
    // https://github.com/nefarius/ViGEm/blob/master/NET/HidCerberus.Srv/NancyFx/Modules/HidGuardianNancyModuleV1.cs

    public class HidGuardianHelper
    {
        static string HidGuardianRegistryKeyBase => @"SYSTEM\CurrentControlSet\Services\HidGuardian\Parameters";

        static readonly IEnumerable<object> ResponseOk = new[] { "OK" };
        static readonly string[] HardwareIdSplitters = { "\r\n", "\n" };

        static readonly Regex HardwareIdRegex = new Regex(@"HID\\[{(]?[0-9A-Fa-z]{8}[-]?([0-9A-Fa-z]{4}[-]?){3}[0-9A-Fa-z]{12}[)}]?|HID\\VID_[a-zA-Z0-9]{4}&PID_[a-zA-Z0-9]{4}");
        static readonly Regex UsbRegex = new Regex(@"\\{2}\?\\(hid)#(vid_[a-z0-9]{4}&pid_[a-z0-9]{4}[^#]*)");
        static readonly Regex BluetoothRegex = new Regex(@"\\{2}\?\\(hid)#([{(]?[0-9A-Fa-z]{8}[-]?([0-9A-Fa-z]{4}[-]?){3}[0-9A-Fa-z]{12}[)}]?_vid&[a-z0-9]{8}_pid&[^#]*)");

        public static string GetHardwareId(string devicePath)
        {
            var regexes = new[]
            {
                // USB notation
                UsbRegex,
                // Bluetooth service notation
                BluetoothRegex
            };
            foreach (var regex in regexes)
            {
                if (regex.IsMatch(devicePath))
                    return $"{regex.Match(devicePath).Groups[1].Value}\\{regex.Match(devicePath).Groups[2].Value}".ToUpper();
            }
            return string.Empty;
        }

        #region WhiteList

        static string HidWhitelistRegistryKeyBase => $"{HidGuardianRegistryKeyBase}\\Whitelist";

        /// <summary>
        /// Allows application to see all hidden controllers.
        /// </summary>
        public static bool AddCurrentProcessToWhiteList()
        {
            var id = System.Diagnostics.Process.GetCurrentProcess().Id;
            return InsertToWhiteList(id);
        }

        /// <summary>
        /// Denies application to see all hidden controllers.
        /// </summary>
        public static bool RemoveCurrentProcessFromWhiteList()
        {
            var id = System.Diagnostics.Process.GetCurrentProcess().Id;
            return InsertToWhiteList(id);
        }

        /// <summary>
        /// Insert process ID into white list. This will allow for application to see all controllers.
        /// </summary>
        /// <param name="processId">Application process Id</param>
        public static bool InsertToWhiteList(int processId)
        {
            // Make sure that 32/64-bit application opens correct registry.
            var view = Environment.Is64BitOperatingSystem
                ? RegistryView.Registry64
                : RegistryView.Registry32;
            var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            var key = baseKey.CreateSubKey($"{HidWhitelistRegistryKeyBase}\\{processId}");
            if (key != null)
                key.Close();
            baseKey.Close();
            return true;
        }

        /// <summary>
        /// Remove process ID from white list.
        /// </summary>
        /// <param name="processId"></param>
        public static bool RemoveFromWhiteList(int processId)
        {
            Registry.LocalMachine.DeleteSubKey($"{HidWhitelistRegistryKeyBase}\\{processId}");
            return true;
        }

        public static int[] SelectFromWhiteList()
        {
            var list = new List<int>();
            var key = Registry.LocalMachine.OpenSubKey(HidWhitelistRegistryKeyBase);
            if (key == null)
                return list.ToArray();
            var names = key.GetSubKeyNames();
            foreach (var name in names)
            {
                int id;
                if (int.TryParse(name, out id))
                    list.Add(id);
            }
            key.Close();
            return list.ToArray();
        }

        /// <summary>
        /// Remove all process IDs from whitelist.
        /// </summary>
        public static bool ClearWhiteList(bool keepCurrentProcess, bool keepRunningProcesses)
        {
            var key = Registry.LocalMachine.OpenSubKey(HidWhitelistRegistryKeyBase);
            if (key == null)
                return true;
            var keepIds = new List<int>();
            if (keepCurrentProcess)
            {
                var id = System.Diagnostics.Process.GetCurrentProcess().Id;
                keepIds.Add(id);
            }
            if (keepRunningProcesses)
            {
                var ids = System.Diagnostics.Process.GetProcesses().Select(x => x.Id);
                keepIds.AddRange(ids);
            }
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                int processId;
                if (int.TryParse(subKeyName, out processId) && keepIds.Contains(processId))
                    continue;
                Registry.LocalMachine.DeleteSubKey($"{HidWhitelistRegistryKeyBase}\\{subKeyName}");
            }
            key.Close();
            return true;
        }

        #endregion

        #region Force

        public static bool GetForce()
        {
            var key = Registry.LocalMachine.OpenSubKey(HidGuardianRegistryKeyBase);
            if (key == null)
                return false;
            var force = (int)key.GetValue("Force", 0) == 1;
            key.Close();
            return force;
        }

        public static void SetForce(bool enabled)
        {
            var key = Registry.LocalMachine.OpenSubKey(HidGuardianRegistryKeyBase);
            if (key == null)
                return;
            key.SetValue("Force", enabled ? 1 : 0);
            key.Close();
        }

        #endregion

        #region Affected

        public static bool InsertToAffected(params string[] hwIds)
        {
            // Return if invalid id found.
            if (hwIds.Any(i => !HardwareIdRegex.IsMatch(i)))
                return false;
            // Get existing Hardware IDs.
            var key = Registry.LocalMachine.CreateSubKey(HidGuardianRegistryKeyBase);
            var current = (key.GetValue("AffectedDevices", new string[0]) as string[]).ToList();
            // Combine arrays.
            current.AddRange(hwIds);
            // Get unique and sorted list.
            var newList = current
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .OrderBy(x => x)
                .ToArray();
            // Write back to registry.
            key.SetValue("AffectedDevices", newList, RegistryValueKind.MultiString);
            key.Close();
            return true;
        }

        public static bool RemoveFromAffected(params string[] hwIds)
        {
            // Return if invalid id found.
            if (hwIds.Any(i => !HardwareIdRegex.IsMatch(i)))
                return false;
            // Get existing Hardware IDs.
            var key = Registry.LocalMachine.CreateSubKey(HidGuardianRegistryKeyBase);
            var current = (key.GetValue("AffectedDevices", new string[0]) as string[]).ToList();
            // Remove values from array.
            current.RemoveAll(x => hwIds.Contains(x));
            // Get unique and sorted list.
            var newList = current
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .OrderBy(x => x)
                .ToArray();
            // Write back to registry.
            key.SetValue("AffectedDevices", newList, RegistryValueKind.MultiString);
            key.Close();
            return true;
        }

        public static string[] GetAffected()
        {
            var key = Registry.LocalMachine.OpenSubKey(HidGuardianRegistryKeyBase, false);
            if (key == null)
                return new string[0];
            var current = key.GetValue("AffectedDevices", new string[0]) as string[];
            key.Close();
            return current;
        }

        public static bool ClearAffected()
        {
            var key = Registry.LocalMachine.OpenSubKey(HidGuardianRegistryKeyBase, true);
            if (key == null)
                return true;
            key.SetValue("AffectedDevices", new string[0], RegistryValueKind.MultiString);
            key.Close();
            return true;
        }

        #endregion

    };

}

