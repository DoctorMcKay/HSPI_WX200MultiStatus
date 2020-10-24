// ReSharper disable InconsistentNaming
namespace HSPI_WX200MultiStatus.Enums {
	public enum WX200ConfigParam : byte {
		// We're only including parameters relevant to our use-case
		StatusModeActive = 13,
		NormalModeLedColor = 14,
		StatusModeLed1Color = 21, // Single LED for WS200+
		StatusModeLed2Color = 22,
		StatusModeLed3Color = 23,
		StatusModeLed4Color = 24,
		StatusModeLed5Color = 25,
		StatusModeLed6Color = 26,
		StatusModeLed7Color = 27,
		WDFCStatusModeBlinkFrequency = 30, // Blink frequency for WD and FC
		WDFCStatusModeBlinkBitmask = 31, // Bitmask only applies to WD and FC
		WSStatusModeBlinkFrequency = 31 // Blink frequency for WS
	}
}
