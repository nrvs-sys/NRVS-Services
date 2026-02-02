using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public static class AchievementKeys
{
    // basic
    public const string ArcaneGenesis           = "arcane_genesis";
    public const string TrainingComplete        = "training_complete";

    // solo, duo, and trio scores
    public const string SoloTier1               = "solo_tier1";
    public const string SoloTier2               = "solo_tier2";
    public const string SoloTier3               = "solo_tier3";
    public const string DuoTier1                = "duo_tier1";
    public const string DuoTier2                = "duo_tier2";
    public const string DuoTier3                = "duo_tier3";
    public const string TrioTier1               = "trio_tier1";
    public const string TrioTier2               = "trio_tier2";
    public const string TrioTier3               = "trio_tier3";

    // boss kills
    public const string SoloBossKill            = "solo_boss_kill";
    public const string DuoBossKill             = "duo_boss_kill";
    public const string TrioBossKill            = "trio_boss_kill";

    // rune based t3 scores
    public const string RuneBalancedT3          = "rune_balanced_t3";
    public const string RuneOceanicT3           = "rune_oceanic_t3";
    public const string RuneVolcanicT3          = "rune_volcanic_t3";
    public const string RuneLightningT3         = "rune_lightning_t3";
    public const string RunePulsarT3            = "rune_pulsar_t3";
    public const string RuneOceanicVolcanicT3   = "rune_oceanic_volcanic_t3";
    public const string RuneOceanicLightningT3  = "rune_oceanic_lightning_t3";
    public const string RuneOceanicPulsarT3     = "rune_oceanic_pulsar_t3";
    public const string RuneVolcanicLightningT3 = "rune_volcanic_lightning_t3";
    public const string RuneVolcanicPulsarT3    = "rune_volcanic_pulsar_t3";
    public const string RuneLightningPulsarT3   = "rune_lightning_pulsar_t3";

    // orb milestones
    public const string Orbs1K                  = "orbs_1k";
    public const string Orbs10K                 = "orbs_10k";
    public const string Orbs100K                = "orbs_100k";
    public const string Orbs1M                  = "orbs_1m";

    // mastery
    public const string SoloRunFlawless         = "solo_run_flawless";
    public const string SoloBossFlawless        = "solo_boss_flawless";
    public const string AllEmblemsT3            = "all_emblems_t3";

    // Steam stats
    public const string OrbsCollected           = "orbs_collected";


    public static readonly string[] RuneKeys =
    {
        AchievementKeys.RuneBalancedT3,
        AchievementKeys.RuneOceanicT3,
        AchievementKeys.RuneVolcanicT3,
        AchievementKeys.RuneLightningT3,
        AchievementKeys.RunePulsarT3,
        AchievementKeys.RuneOceanicVolcanicT3,
        AchievementKeys.RuneOceanicLightningT3,
        AchievementKeys.RuneOceanicPulsarT3,
        AchievementKeys.RuneVolcanicLightningT3,
        AchievementKeys.RuneVolcanicPulsarT3,
        AchievementKeys.RuneLightningPulsarT3
    };
}