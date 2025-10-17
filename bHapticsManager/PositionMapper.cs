// PositionMapper.cs
// Maps between legacy and modern bHaptics position types

using LegacyBHaptics = Bhaptics.Tact;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	
	/// Utility class for mapping between legacy (Bhaptics.Tact) and modern (bHapticsLib) position types.
	
	public static class PositionMapper {
		
		/// Maps modern PositionID to legacy PositionType.
		
		public static LegacyBHaptics.PositionType MapModernToLegacy(ModernBHaptics.PositionID modernPos) {
			return modernPos switch {
				ModernBHaptics.PositionID.Head => LegacyBHaptics.PositionType.Head,
				ModernBHaptics.PositionID.Vest => LegacyBHaptics.PositionType.Vest,
				ModernBHaptics.PositionID.VestFront => LegacyBHaptics.PositionType.VestFront,
				ModernBHaptics.PositionID.VestBack => LegacyBHaptics.PositionType.VestBack,
				ModernBHaptics.PositionID.ArmLeft => LegacyBHaptics.PositionType.ForearmL,
				ModernBHaptics.PositionID.ArmRight => LegacyBHaptics.PositionType.ForearmR,
				ModernBHaptics.PositionID.HandLeft => LegacyBHaptics.PositionType.HandL,
				ModernBHaptics.PositionID.HandRight => LegacyBHaptics.PositionType.HandR,
				ModernBHaptics.PositionID.FootLeft => LegacyBHaptics.PositionType.FootL,
				ModernBHaptics.PositionID.FootRight => LegacyBHaptics.PositionType.FootR,
				_ => LegacyBHaptics.PositionType.Vest
			};
		}

		
		/// Maps legacy PositionType to modern PositionID.
		
		public static ModernBHaptics.PositionID MapLegacyToModern(LegacyBHaptics.PositionType legacyType) {
			return legacyType switch {
				LegacyBHaptics.PositionType.Head => ModernBHaptics.PositionID.Head,
				LegacyBHaptics.PositionType.Vest => ModernBHaptics.PositionID.Vest,
				LegacyBHaptics.PositionType.VestFront => ModernBHaptics.PositionID.VestFront,
				LegacyBHaptics.PositionType.VestBack => ModernBHaptics.PositionID.VestBack,
				LegacyBHaptics.PositionType.ForearmL => ModernBHaptics.PositionID.ArmLeft,
				LegacyBHaptics.PositionType.ForearmR => ModernBHaptics.PositionID.ArmRight,
				LegacyBHaptics.PositionType.HandL => ModernBHaptics.PositionID.HandLeft,
				LegacyBHaptics.PositionType.HandR => ModernBHaptics.PositionID.HandRight,
				LegacyBHaptics.PositionType.FootL => ModernBHaptics.PositionID.FootLeft,
				LegacyBHaptics.PositionType.FootR => ModernBHaptics.PositionID.FootRight,
				_ => ModernBHaptics.PositionID.Vest
			};
		}

		
		/// Converts a list of legacy DotPoints to modern DotPoints.
		
		public static List<ModernBHaptics.DotPoint> ConvertDotPoints(List<LegacyBHaptics.DotPoint> legacyPoints) {
			if (legacyPoints == null || legacyPoints.Count == 0)
				return null;

			var modernPoints = new List<ModernBHaptics.DotPoint>(legacyPoints.Count);
			foreach (var legacy in legacyPoints) {
				modernPoints.Add(new ModernBHaptics.DotPoint(legacy.Index, legacy.Intensity));
			}
			return modernPoints;
		}

		/// Converts a list of legacy DotPoints to a direct motor intensity array.
		/// bHapticsLib expects int[] in range 0-500, but we can also use byte[] 0-200.
		/// FrooxEngine sends 0-100, so we need to scale up.
		public static byte[] ConvertDotPointsToMotorArray(List<LegacyBHaptics.DotPoint> legacyPoints, ModernBHaptics.PositionID position) {
			if (legacyPoints == null || legacyPoints.Count == 0)
				return null;

			// Determine motor count based on device type
			int motorCount = position switch {
				ModernBHaptics.PositionID.Vest => 40,
				ModernBHaptics.PositionID.VestFront => 20,
				ModernBHaptics.PositionID.VestBack => 20,
				ModernBHaptics.PositionID.Head => 20,
				ModernBHaptics.PositionID.ArmLeft => 6,
				ModernBHaptics.PositionID.ArmRight => 6,
				ModernBHaptics.PositionID.HandLeft => 6,
				ModernBHaptics.PositionID.HandRight => 6,
				ModernBHaptics.PositionID.FootLeft => 3,
				ModernBHaptics.PositionID.FootRight => 3,
				ModernBHaptics.PositionID.GloveLeft => 10,
				ModernBHaptics.PositionID.GloveRight => 10,
				_ => 20
			};

			// Use byte[] (0-200 range) for better compatibility
			byte[] motors = new byte[motorCount];
			
			// Convert FrooxEngine intensity (0-100) to bHaptics range (0-200)
			foreach (var point in legacyPoints) {
				if (point.Index >= 0 && point.Index < motorCount) {
					// Scale: 0-100 -> 0-200 (multiply by 2)
					motors[point.Index] = (byte)Math.Min(200, point.Intensity * 2);
				}
			}
			
			return motors;
		}
	}
}
