# The Last Watch Zombies — Full Config Reference

The single mod writes two JSON files under `VintagestoryData/ModConfig/`. Defaults below are taken from the merged source inputs; runtime validation and legacy-default normalization should be checked in-game.

## Main gameplay settings

| JSON key | Default |
|---|---|
| `ZombieIdleDespawnSeconds` | `300.0` |
| `NpcTraderInterestSeconds` | `120.0` |
| `ZombieStayActiveNearPlayers` | `true` |
| `ZombieKeepActiveRadiusBlocks` | `64` |
| `ZombiePresenceCheckSeconds` | `10.0` |
| `ZombieLostTargetPursuitSeconds` | `25.0` |
| `HordeStartHour` | `18.0` |
| `HordeMinutesBeforeStartHour` | `5.0` |
| `NormalHitsToInfect` | `4` |
| `AlphaHitsToInfect` | `2` |
| `HitWindowSeconds` | `60.0` |
| `InfectionTickSeconds` | `10.0` |
| `InfectionDamagePerTick` | `0.2` |
| `InfectionFatalEnabled` | `true` |
| `InfectionFatalDays` | `7.0` |
| `CureDosesRequired` | `6` |
| `CureDoseIntervalDays` | `1.0` |
| `TreatedInfectionTickSeconds` | `20.0` |
| `CurePsychedelicEnabled` | `true` |
| `CorpseDelaySeconds` | `30.0` |
| `CorpseRot` | `3` |
| `CorpseBones` | `2` |
| `CorpseSmallHides` | `1` |
| `AlphaFuneralMaskChanceDenominator` | `10` |
| `FuneralMask1Durability` | `200` |
| `OtherFuneralMaskDurability` | `50` |
| `AlphaFuneralMaskItemCodes` | `["game:clothes-face-funeralmask1", "game:clothes-face-funeralmask2", "game:clothes-face-funeralmask3", "game:clothes-face-funeralmask4", "game:clothes-face-funeralmask5", "game:clothes-face-funeralmask6", "game:clothes-face-funeralmask7", "game:clothes-face-funeralmask8"]` |
| `AlphaSpeedMultiplier` | `1.65` |
| `AlphaAttackDamageMultiplier` | `1.25` |
| `MaxActiveZombies` | `250` |
| `ZombieSpawnHordeNightOnly` | `false` |
| `ZombieSpawnNightOnly` | `true` |
| `HordeEveryDays` | `7` |
| `HordeDurationSeconds` | `600.0` |
| `HordeWaveIntervalSeconds` | `180.0` |
| `HordeWaveMinZombiesPerPlayer` | `1` |
| `HordeInitialMinZombiesPerPlayer` | `3` |
| `HordeWaveMaxZombiesPerPlayer` | `5` |
| `HordeInitialRetrySeconds` | `10.0` |
| `HordeInitialRetryWindowSeconds` | `120.0` |
| `SpawnMaxLightLevel` | `12` |
| `HordeSpawnIgnoreLightLevel` | `false` |
| `HordeSpawnsInClaims` | `false` |
| `SpawnAlphaOnPlayerDeath` | `true` |
| `NightStartsAtHour` | `18.0` |
| `NightEndsAtHour` | `6.0` |
| `AmbientSpawnRequiresNight` | `true` |
| `AlphaDetectionRangeBonus` | `32.0` |
| `SpawnMinDistance` | `20` |
| `SpawnMaxDistance` | `48` |
| `TargetWhitelist` | `[]` |
| `TargetBlacklist` | `[]` |
| `IgnoreVanillaAnimals` | `true` |
| `DetectionRange` | `32.0` |
| `HordeDetectionRange` | `80.0` |
| `MoveSpeed` | `0.018` |
| `AttackDamage` | `2.0` |
| `AttackCooldownSeconds` | `2.5` |
| `BlockBreakingEnabled` | `true` |
| `BlockBreakIntervalSeconds` | `2.5` |
| `BlockBreakChance` | `0.65` |
| `HordeBlockBreakChance` | `0.9` |
| `NonHordeBlockReinforcementDamage` | `1` |
| `HordeBlockReinforcementDamage` | `2` |
| `HordeOpensDoors` | `true` |
| `ProtectClaims` | `true` |
| `ProtectReinforcedBlocks` | `false` |
| `AmbientSpawnChancePerSecond` | `0.3` |
| `zombiepopdailyaddlimit` | `5` |
| `DormantPopulationCap` | `250` |
| `DormantWakeOnlyInSameZone` | `true` |
| `DormantReactivationPerZonePerDay` | `0` |
| `DormantWakeBatchSize` | `3` |
| `ZoneMigrationHour` | `18` |
| `HordeMigrationMaxZoneSteps` | `2` |
| `ZoneBaseDailyMigrationChance` | `0.02` |
| `ZoneMaxDailyMigrationChance` | `0.85` |
| `ZonePlayerPresenceBonus` | `0.1` |
| `ZoneSettlementBonus` | `0.3` |
| `ZoneStrongholdBonus` | `0.4` |
| `ZoneBonfireAttractionBonus` | `0.7` |
| `ZoneStonePathBonusPerBlock` | `0.002` |
| `ZoneDeathBonusPerDeath` | `0.075` |
| `ZoneTraderDormantSeed` | `10` |
| `ZoneMigrationChatAnnouncement` | `true` |
| `ZoneBellRangeZones` | `1` |
| `ZoneBellOverrunThreshold` | `30` |
| `ZoneFarmAnimalThreshold` | `10` |
| `ZoneFarmAnimalAttractionBonus` | `0.12` |
| `DormantWakeIntervalSeconds` | `5` |

## Advanced settings

| JSON key | Default |
|---|---|
| `PerformanceDiagnostics` | `false` |
| `AiPreyScansPerSecond` | `160` |
| `AiRouteRequestsPerSecond` | `60` |
| `AiPreyScanIntervalSeconds` | `0.85` |
| `PursuitSteerIntervalSeconds` | `0.55` |
| `PursuitDirectWalkRangeBlocks` | `10.0` |
| `PursuitRepathTargetMovementBlocks` | `1.5` |
| `PursuitResponsivenessVersion` | `0` |
| `HordeSpawnAlignmentVersion` | `0` |
| `HitsToInfect` | `3` |
| `CurePsychedelicConfigVersion` | `0` |
| `CureBalanceConfigVersion` | `0` |
| `QuietEyeBandageItemIds` | `[3889, 3890]` |
| `MaskDrainSeconds` | `1.0` |
| `RotTunicFirstWearDamage` | `3.0` |
| `RotTunicDrainSeconds` | `1.0` |
| `MaskBalanceConfigVersion` | `0` |
| `SpawnLightConfigVersion` | `0` |
| `SpawnMinimumOtherPlayerDistance` | `12` |
| `SpawnDebugLogging` | `true` |
| `SpawnPositionAttempts` | `64` |
| `AmbientSpawnSurfaceOnly` | `true` |
| `SpawnSurfaceVerticalTolerance` | `5` |
| `SpawnPerformanceTuningVersion` | `0` |
| `AlphaDeathSpawnDelayMilliseconds` | `1200` |
| `SpawnDistanceTuningVersion` | `1` |
| `ManualHordeSoundRange` | `38.0` |
| `ManualHordeMoans` | `3` |
| `ManualHordeZombieDetectionRadius` | `50.0` |
| `EyeBandageCodeFragments` | `["eyepatch", "eye-patch", "eye-bandage", "eyebandage", "eyeband", "bandage-eye", "patch-eye", "eyecover", "eye-cover"]` |
| `EyeBandageInventoryClasses` | `["character", "accessory", "equipment", "wearable"]` |
| `FollowAlphaEnabled` | `true` |
| `AlphaFollowRange` | `32.0` |
| `AlphaFollowStopDistance` | `3.0` |
| `AlphaFollowRefreshSeconds` | `3.0` |
| `SneakDetectionRangeMultiplier` | `0.4` |
| `SneakAcquireChance` | `0.25` |
| `SneakLoseTargetChance` | `0.35` |
| `SneakAlwaysDetectedDistance` | `4.0` |
| `RoamEnabled` | `true` |
| `RoamWalkSeconds` | `30.0` |
| `RoamPauseSeconds` | `10.0` |
| `RoamRouteRefreshSeconds` | `4.0` |
| `RoamSpeedMultiplier` | `0.55` |
| `LostSearchTimingConfigVersion` | `0` |
| `SiegeBalanceConfigVersion` | `0` |
| `SiegeEnabled` | `true` |
| `SiegeProgressSampleSeconds` | `2.5` |
| `SiegeStuckSeconds` | `5.0` |
| `SiegeMinimumTravelBlocks` | `0.4` |
| `SiegeNavigationRefreshSeconds` | `2.0` |
| `BlockBreakReach` | `2.2` |
| `HordeDoorAttemptCooldownSeconds` | `2.0` |
| `MoanMinSeconds` | `1.0` |
| `MoanMaxSeconds` | `30.0` |
| `ZombieMoanVolume` | `0.9` |
| `ZombieHurtSoundVolume` | `0.85` |
| `ZombieHurtSoundCooldownSeconds` | `0.65` |
| `ZoneMaxPathsCounted` | `300` |
| `LastWatchVerboseLogging` | `false` |
| `ZoneAttractionTier4Threshold` | `0.2` |
| `ZoneAttractionTier6Threshold` | `0.4` |
| `ZoneAttractionTier8Threshold` | `0.65` |
| `ZoneAttractionTier10Threshold` | `0.9` |
| `ZoneStayWeight` | `0.35` |
| `ZoneRandomNeighborWeight` | `0.65` |
| `ZoneForgeActivityBonus` | `0.08` |
| `ZoneCharcoalActivityBonus` | `0.1` |
| `ZoneAnvilActivityBonus` | `0.06` |
| `ZoneIndustrySurfaceBelowBlocks` | `8` |
| `ZoneIndustrySurfaceAboveBlocks` | `24` |
| `DormantWakeRadiusChunks` | `5` |
| `DormantDebugLogging` | `false` |
| `DormantMaxChunkAttemptsPerCheck` | `16` |
| `DormantMaxTotalWakesPerPass` | `8` |

## New variant settings (main config)

| Key | Default |
|---|---:|
| GrabberSpawnChance | 0.04 |
| HiveSpawnChance | 0.02 |
| ScreamerSpawnChance | 0.01 |
| BloaterSpawnChance | 0.03 |
| GrabberLeapCooldownSeconds | 8 |
| HiveBeeCooldownSeconds | 30 |
| BloaterBurstRadius | 4 |
| BloaterBurstDamage | 4 |
| BloaterRotDrop | 32 |
| BloaterPoisonSeconds | 10 |
| BloaterPoisonDamagePerSecond | 0.5 |

Spawn pause is immediate world state, controlled by `/zombiepause` and `/zombieunpause`, not a restart-only config field.
