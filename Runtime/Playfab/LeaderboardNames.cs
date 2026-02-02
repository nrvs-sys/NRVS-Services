using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// String names for PlayFab leaderboards used by our <see cref="LeaderboardController"/> to assign localized strings
/// </summary>
public static class LeaderboardNames
{
	public const string AscensionP1 = "Ascension_P1";
	public const string AscensionP2 = "Ascension_P2";
	public const string AscensionP3 = "Ascension_P3";

	public const string SurvivalP1NightsideGateway = "Survival_P1_NightsideGateway";
	public const string SurvivalP2NightsideGateway = "Survival_P2_NightsideGateway";
	public const string SurvivalP3NightsideGateway = "Survival_P3_NightsideGateway";

	public const string SurvivalP1TheUnderstory = "Survival_P1_TheUnderstory";
	public const string SurvivalP2TheUnderstory = "Survival_P2_TheUnderstory";
	public const string SurvivalP3TheUnderstory = "Survival_P3_TheUnderstory";

	public const string SurvivalP1ShatteredSky = "Survival_P1_ShatteredSky";
	public const string SurvivalP2ShatteredSky = "Survival_P2_ShatteredSky";
	public const string SurvivalP3ShatteredSky = "Survival_P3_ShatteredSky";

	public const string SurvivalP1TwilightDunes = "Survival_P1_TwilightDunes";
	public const string SurvivalP2TwilightDunes = "Survival_P2_TwilightDunes";
	public const string SurvivalP3TwilightDunes = "Survival_P3_TwilightDunes";

	public const string SurvivalP1MoltenSpires = "Survival_P1_MoltenSpires";
	public const string SurvivalP2MoltenSpires = "Survival_P2_MoltenSpires";
	public const string SurvivalP3MoltenSpires = "Survival_P3_MoltenSpires";

	public const string SurvivalP1TheDarkWell = "Survival_P1_TheDarkWell";
	public const string SurvivalP2TheDarkWell = "Survival_P2_TheDarkWell";
	public const string SurvivalP3TheDarkWell = "Survival_P3_TheDarkWell";

	/// <summary>
	/// All leaderboard names
	/// </summary>
	public static IReadOnlyList<string> All { get; } =
		typeof(LeaderboardNames)
			.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
			.Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
			.Select(f => (string)f.GetRawConstantValue())
			.ToArray();
}