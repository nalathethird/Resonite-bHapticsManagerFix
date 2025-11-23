
using LegacyBHaptics = Bhaptics.Tact;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	
	public static class PositionMapper {
		
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

		public static int[]? ConvertDotPointsToMotorArray(List<LegacyBHaptics.DotPoint> legacyPoints, ModernBHaptics.PositionID position) {
			if (legacyPoints == null || legacyPoints.Count == 0)
				return null;

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

			int[] motors = new int[motorCount];
			
			foreach (var point in legacyPoints) {
				if (point.Index >= 0 && point.Index < motorCount) {
					motors[point.Index] = Math.Min(200, point.Intensity * 2);
				}
			}
			
			return motors;
		}
	}
}
