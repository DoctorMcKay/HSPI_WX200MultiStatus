using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HomeSeer.PluginSdk;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Logging;
using HSPI_WX200MultiStatus.Enums;

namespace HSPI_WX200MultiStatus.DataStructures {
	// ReSharper disable once InconsistentNaming
	public class WX200Device {
		public readonly WX200DeviceType Type;
		public readonly int DevRef;
		public readonly string HomeId;
		public readonly byte NodeId;
		public readonly List<string> Groups;
		private readonly HSPI _plugin;
		
		private bool _hasSyncedState = false;
		private readonly byte[] _statusLedStates;
		private bool _isInStatusMode;
		private byte _blinkMask;

		public WX200Device(HSPI plugin, AbstractHsDevice hsDevice) {
			_plugin = plugin;
			Groups = new List<string>();

			Type = GetDeviceType(hsDevice);
			DevRef = hsDevice.Ref;

			string[] address = hsDevice.Address.Split('-');
			HomeId = address[0];
			NodeId = byte.Parse(address[1]);

			_loadGroups();

			_statusLedStates = new byte[GetLedCount()];
		}

		private void _loadGroups() {
			Dictionary<string, string> section = _plugin.GetIniSection(_getIniSectionName());
			foreach (string groupName in section.Keys.Where(groupName => section[groupName] == "1")) {
				Groups.Add(groupName);
			}
		}

		public void SaveGroups() {
			string section = _getIniSectionName();
			_plugin.ClearIniSection(section);
			foreach (string groupName in Groups) {
				_plugin.SaveIniSetting(section, groupName, "1");
			}
		}

		public void SetStatusLed(byte ledIndex, WX200StatusModeColor color, bool blink) {
			SyncState();
			if (ledIndex >= GetLedCount()) {
				throw new Exception($"LED index {ledIndex} is out of range (max {GetLedCount()}");
			}

			_statusLedStates[ledIndex] = (byte) color;
			if (blink) {
				_blinkMask |= (byte) (1 << ledIndex);
			} else {
				_blinkMask &= (byte) ~(1 << ledIndex);
			}
			
			_plugin.WriteLog(ELogType.Debug, $"Value for {HomeId}:{NodeId} LED {ledIndex} is now {_statusLedStates[ledIndex]} with blink mask {_blinkMask}");

			_plugin.ConfigSet(HomeId, NodeId, (byte) (WX200ConfigParam.StatusModeLed1Color + ledIndex), 1, _statusLedStates[ledIndex]);
			if (Type == WX200DeviceType.WS200) {
				_plugin.ConfigSet(HomeId, NodeId, (byte) WX200ConfigParam.WSStatusModeBlinkFrequency, 1, (blink ? 1 : 0) * _plugin.WsBlinkFrequency);
			} else {
				_plugin.ConfigSet(HomeId, NodeId, (byte) WX200ConfigParam.WDFCStatusModeBlinkBitmask, 1, _blinkMask);
			}

			_syncStatusMode();
		}

		private string _getIniSectionName() {
			return $"Groups_{HomeId}_{NodeId}";
		}

		public byte GetLedCount() {
			switch (Type) {
				case WX200DeviceType.WS200:
					return 1;
				
				case WX200DeviceType.WD200:
					return 7;
				
				case WX200DeviceType.FC200:
					return 4;
				
				default:
					throw new Exception("Unknown type");
			}
		}

		internal void SyncState() {
			if (_hasSyncedState) {
				return;
			}

			for (byte i = 0; i < GetLedCount(); i++) {
				_statusLedStates[i] = (byte) _plugin.ConfigGet(HomeId, NodeId, (byte) (WX200ConfigParam.StatusModeLed1Color + i));
			}

			_isInStatusMode = _plugin.ConfigGet(HomeId, NodeId, (byte) WX200ConfigParam.StatusModeActive) == 1;

			// Blink mask is not used for WS200+ so don't waste time retrieving it
			if (Type != WX200DeviceType.WS200) {
				_blinkMask = (byte) _plugin.ConfigGet(HomeId, NodeId, (byte) WX200ConfigParam.WDFCStatusModeBlinkBitmask);
			}

			_hasSyncedState = true;
		}

		private void _syncStatusMode() {
			bool shouldStatusMode = _statusLedStates.Any(val => val != (byte) WX200StatusModeColor.Off);
			if (shouldStatusMode != _isInStatusMode) {
				_plugin.ConfigSet(HomeId, NodeId, (byte) WX200ConfigParam.StatusModeActive, 1, shouldStatusMode ? 1 : 0);
				_isInStatusMode = shouldStatusMode;
			}
		}

		public static WX200DeviceType GetDeviceType(AbstractHsDevice hsDevice) {
			if (hsDevice.Interface != "Z-Wave") {
				throw new Exception("Provided device is not a Z-Wave device");
			}
			
			int? manufacturerId = hsDevice.PlugExtraData.GetNamed<int?>("manufacturer_id");
			ushort? prodId = hsDevice.PlugExtraData.GetNamed<ushort?>("manufacturer_prod_id");
			ushort? prodType = hsDevice.PlugExtraData.GetNamed<ushort?>("manufacturer_prod_type");

			if (manufacturerId != 12) {
				// All HS devices have manufacturer ID 12
				throw new Exception("Provided device is not a HS-WX200+");
			}

			if (prodId == 12341 && prodType == 17479) {
				return WX200DeviceType.WS200;
			}

			if (prodId == 12342 && prodType == 17479) {
				return WX200DeviceType.WD200;
			}

			if (prodId == 1 && prodType == 515) {
				return WX200DeviceType.FC200;
			}

			throw new Exception("Provided device is not a HS-WX200+");
		}

		// ReSharper disable once InconsistentNaming
		public static bool IsWX200Device(AbstractHsDevice hsDevice) {
			try {
				GetDeviceType(hsDevice);
				return true;
			} catch (Exception) {
				return false;
			}
		}
	}
}
