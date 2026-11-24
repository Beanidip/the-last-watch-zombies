using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.Essentials;
using Vintagestory.API.Config;

namespace SeventhNightZombies;

public partial class Zombie : EntityAgent
{
    // Three inexpensive choices: chase prey, follow an Alpha, or idle in the last direction.
    // Navigation uses Vintage Story's WaypointsTraverser; no custom player motion.
    SeventhNightSystem system;
    WaypointsTraverser path;
    Entity target;
    Zombie alphaLeader;
    Vec3d idleDirection;
    Vec3d lastKnownPreyPosition;
    long lostPreyUntilMs, nextLostPreyRouteMs, nextPresenceCheckMs;
    bool nearbySurvivalPlayer;
    Vec3d lastProgressPosition;
    Vec3d lastChaseRouteDestination;
    long nextDirectSteerMs;
    bool directPursuitActive;
    long nextPreyScanMs, nextRouteMs, nextIdleRouteMs, nextFollowScanMs;
    float attack, moan, doorCooldown, hurtSoundCooldown;
    double stationarySeconds, progressSampleSeconds, breakTimer;
    double idleDespawnSeconds;
    // Timed, per-zombie NPC/trader attention budget. Tracking a PLAYER pauses it;
    // losing that player is the sole event that grants a fresh two minutes.
    const string NpcInterestStartedKey = "seventhnight-npc-interest-started";
    const string NpcInterestUsedKey = "seventhnight-npc-interest-used";
    const string NpcInterestExpiredKey = "seventhnight-npc-interest-expired";
    double npcInterestUsedSeconds;
    float npcInterestSaveSeconds;
    bool npcInterestStarted, npcInterestExpired;
    bool blockedRoute, walkingAnimation;
    bool routePending, roamPaused;
    long routeEpoch, pendingRouteSince, roamPhaseUntil;
    // Navigation callbacks belong to one request. A target/mode transition
    // cancels the traverser and invalidates that request's callbacks.
    void StopNavigation()
    {
        routeEpoch++;
        routePending = false;
        directPursuitActive = false;
        lastChaseRouteDestination = null;
        path?.Stop();
    }
    bool RequestRoute(Vec3d destination, float speed, float distance, bool siege, int depth)
    {
        long now = World.ElapsedMilliseconds;
        if (routePending && now - pendingRouteSince > 5000) StopNavigation();
        if (routePending) return false;
        if (!system.AiBudget.TakeRoute(now, C.AiRouteRequestsPerSecond))
        { system.Profiler.Count("ai.routes.deferred"); return false; }
        long request = ++routeEpoch;
        routePending = true; pendingRouteSince = now;
        path.OnFoundPath = () => { if (routeEpoch == request) { routePending = false; blockedRoute = false; } };
        Action failed = () =>
        {
            if (routeEpoch != request) return;
            routePending = false;
            if (siege) blockedRoute = true;
        };
        Action arrived = () => { if (routeEpoch == request) routePending = false; };
        Action noPath = () =>
        {
            if (routeEpoch != request) return;
            failed();
            // The vanilla traverser supplies speed-scaled world-space movement,
            // turning, collision and stuck detection without another path search.
            if (siege) path.WalkTowards(destination, speed, distance, arrived, failed);
        };
        bool accepted = path.NavigateTo_Async(destination, speed, distance,
            arrived, failed, noPath, depth);
        if (!accepted && routeEpoch == request) routePending = false;
        system.Profiler.Count("ai.routes.requested");
        return accepted;
    }
    long RetryAt(long now) => now + 200 + World.Rand.Next(250);

    public const string CorpseAgeKey = "seventhnight-corpse-age";
    public const string LootDroppedKey = "seventhnight-loot-dropped";
    public bool IsDormant { get; set; }
    public bool IsAlpha => Properties?.Code?.Path == "alphazombie";
    // Explicit display name avoids fallback to raw asset codes in hover info,
    // combat feedback, and entity selection. Both strings are localizable.
    public override string GetName()
    {
        string key = "seventhnightzombies:entity-" + ZombieVariantRules.Code(VariantKind);
        string localized = Lang.Get(key);
        // A server without this mod's locale should still display a friendly name.
        return localized == key ? ZombieVariantRules.Name(VariantKind) : localized;
    }
    // Play a short pain grunt only when damage actually reduces the zombie's HP.
    // Server-side playback ensures all nearby players hear the same sound.
    public override bool ReceiveDamage(DamageSource damageSource, float damage)
    {
        var health = GetBehavior<EntityBehaviorHealth>();
        float before = health?.Health ?? 0;
        bool received = base.ReceiveDamage(damageSource, damage);
        if (Api?.Side == EnumAppSide.Server && received && health != null &&
            health.Health < before && hurtSoundCooldown <= 0)
        {
            hurtSoundCooldown = C.ZombieHurtSoundCooldownSeconds;
            int soundIndex = 1 + World.Rand.Next(2);
            World.PlaySoundAt(new AssetLocation($"seventhnightzombies:sounds/hurt{soundIndex}"),
                this, null, C.ZombieHurtSoundVolume, 24, .7f);
        }
        return received;
    }

    public override void Die(EnumDespawnReason reason = EnumDespawnReason.Death, DamageSource damageSourceForDeath = null)
    {
        // Start the 30-second countdown only on a real death. All other despawn reasons
        // (e.g. /entity remove) must never produce free loot.
        if (Alive && reason == EnumDespawnReason.Death && Api?.Side == EnumAppSide.Server)
        {
            WatchedAttributes.SetDouble(CorpseAgeKey, 0);
            WatchedAttributes.RemoveAttribute("seventhnight-sinking"); // old-save cleanup
        }
        bool burst = Alive && reason == EnumDespawnReason.Death && Api?.Side == EnumAppSide.Server;
        base.Die(reason, damageSourceForDeath);
        if (burst && !Alive)
        {
            try { OnVariantKilled(); }
            catch (Exception ex) { Api.Logger.Error("Last Watch: death ability failed: " + ex); }
        }
        // Keep the population cap exact without a global loaded-entity recount.
        if (reason == EnumDespawnReason.Death && Api?.Side == EnumAppSide.Server && !Alive)
        {
            var core = Api.ModLoader.GetModSystem<SeventhNightSystem>();
            core?.UntrackActiveZombie(this);
            core?.Population?.OnZombieDied(this);
        }
    }
    void Drop(string code, int count)
    {
        if (count <= 0) return;
        var item = World.GetItem(new AssetLocation(code));
        if (item == null) { Api.Logger.Warning("Seventh Night Zombies: missing corpse loot item " + code); return; }
        // Drop just above the corpse's last ground position.
        World.SpawnItemEntity(new ItemStack(item, count), Pos.XYZ.AddCopy(0, .2, 0));
    }
    // The server owns both particle broadcast and item drops; the LootDroppedKey
    // guards against duplicate loot on retry / after a save/load.
    void PoofIntoDustAndDropLoot()
    {
        if (WatchedAttributes.GetBool(LootDroppedKey)) return;
        WatchedAttributes.SetBool(LootDroppedKey, true);
        try
        {
            // A short blood-red cubic dust cloud (RGBA color is swapped for
            // VS's BGRA particle byte order). Sent automatically to nearby clients.
            World.SpawnParticles(42f, ColorUtil.ColorFromRgba(30, 28, 170, 225),
                Pos.XYZ.AddCopy(-.35, .15, -.35), Pos.XYZ.AddCopy(.35, 1.65, .35),
                new Vec3f(-.12f, .04f, -.12f), new Vec3f(.12f, .24f, .12f),
                .85f, .12f, .32f, EnumParticleModel.Cube);
        }
        catch (Exception ex)
        {
            Api.Logger.Warning("Seventh Night Zombies: corpse particle effect failed; dropping loot anyway: " + ex.Message);
        }
        Drop("game:rot", C.CorpseRot);
        Drop("game:bone", C.CorpseBones);
        Drop("game:gear-rusty", C.CorpseRustyGears);
        if (IsAlpha)
        {
            if (World.Rand.Next(C.AlphaFuneralMaskChanceDenominator) == 0)
                DropAlphaFuneralMask();
        }
        (Api as Vintagestory.API.Server.ICoreServerAPI)?.World.DespawnEntity(this,
            new EntityDespawnData { Reason = EnumDespawnReason.Removed });
    }
    // Resolve masks by their exact vanilla item codes, not registry scans or
    // world-specific numeric IDs. Keep [] from older configs working by falling
    // back to the known eight game:clothes-face-funeralmask1..8 codes.
    // This is called only after the single 1-in-10 corpse-loot roll succeeds.
    void DropAlphaFuneralMask()
    {
        string[] codes = C.AlphaFuneralMaskItemCodes != null &&
                         C.AlphaFuneralMaskItemCodes.Any(code => !string.IsNullOrWhiteSpace(code))
            ? C.AlphaFuneralMaskItemCodes
            : Config.DefaultAlphaFuneralMaskItemCodes;

        var candidates = codes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Select(code => World.GetItem(new AssetLocation(code.Contains(':') ? code : "game:" + code)))
            .Where(item => item != null)
            .GroupBy(item => item.Code.ToString())
            .Select(group => group.First())
            .ToArray();

        if (candidates.Length == 0)
        {
            Api.Logger.Warning("Seventh Night Zombies: Alpha mask drop rolled successfully, but none " +
                "of the configured funeral mask item codes exist. Verify game:clothes-face-funeralmask1..8 " +
                "and AlphaFuneralMaskItemCodes in ModConfig/thelastwatchzombiesconfigmain.json.");
            return;
        }

        // Pick exactly one valid mask; every registered variant has equal weight.
        var mask = candidates[World.Rand.Next(candidates.Length)];
        World.SpawnItemEntity(new ItemStack(mask, 1), Pos.XYZ.AddCopy(0, .2, 0));
    }
    // Called from the system server tick because dead EntityAgent ticks may stop.
    public void TickCorpse(float dt)
    {
        if (Alive || Api?.Side != EnumAppSide.Server || WatchedAttributes.GetBool(LootDroppedKey)) return;
        double age = WatchedAttributes.GetDouble(CorpseAgeKey) + dt;
        WatchedAttributes.SetDouble(CorpseAgeKey, age);
        if (age >= C.CorpseDelaySeconds) PoofIntoDustAndDropLoot();
    }
    Config C => system.Settings;
    public override void Initialize(EntityProperties properties, ICoreAPI api, long chunkIndex)
    {
        base.Initialize(properties, api, chunkIndex);
        // Reuse the same texture and atlas entry. Set once on each client load;
        // the vanilla shape renderer applies this subtle RGB tint while drawing.
        if (api.Side == EnumAppSide.Client) RenderColor = ZombieVariantRules.Tint(VariantKind);
        if (api.Side != EnumAppSide.Server) return;
        system = api.ModLoader.GetModSystem<SeventhNightSystem>();
        path = new WaypointsTraverser(this);
        LoadPalisadeTrap();
        idleDirection = RandomHorizontalDirection();
        moan = RandomMoanDelay();
        lastProgressPosition = Pos.XYZ.Clone();
        breakTimer = C.BlockBreakIntervalSeconds;
        long now = World.ElapsedMilliseconds;
        nextPreyScanMs = now + World.Rand.Next(500);
        nextFollowScanMs = now + World.Rand.Next(1500);
        nextIdleRouteMs = now + World.Rand.Next(3000);
        nextPresenceCheckMs = now + World.Rand.Next(1000);
        // WatchedAttributes survive normal chunk unload and server saves.
        npcInterestStarted = WatchedAttributes.GetBool(NpcInterestStartedKey);
        npcInterestExpired = WatchedAttributes.GetBool(NpcInterestExpiredKey);
        npcInterestUsedSeconds = Math.Clamp(WatchedAttributes.GetDouble(NpcInterestUsedKey),
            0, C.NpcTraderInterestSeconds);
        if (npcInterestStarted && npcInterestUsedSeconds >= C.NpcTraderInterestSeconds)
            npcInterestExpired = true;
        npcInterestSaveSeconds = 10;
    }

    static bool IsNpcOrTrader(Entity candidate) =>
        candidate != null && candidate is not EntityPlayer && SeventhNightSystem.IsHumanoidPrey(candidate);

    void SaveNpcInterest()
    {
        WatchedAttributes.SetBool(NpcInterestStartedKey, npcInterestStarted);
        WatchedAttributes.SetBool(NpcInterestExpiredKey, npcInterestExpired);
        WatchedAttributes.SetDouble(NpcInterestUsedKey, npcInterestUsedSeconds);
    }

    void StartNpcInterest()
    {
        if (npcInterestStarted || npcInterestExpired) return;
        npcInterestStarted = true;
        npcInterestUsedSeconds = 0;
        npcInterestSaveSeconds = 10;
        SaveNpcInterest();
    }

    void ResetNpcInterestAfterPlayerLost()
    {
        npcInterestStarted = npcInterestExpired = false;
        npcInterestUsedSeconds = 0;
        npcInterestSaveSeconds = 10;
        SaveNpcInterest();
    }

    // Called even if an NPC steps out of range: leaving briefly must not renew
    // its two-minute attention window. One small update per entity tick; only
    // synchronize persistent state every ten seconds, not every tick.
    void TickNpcInterest(float dt)
    {
        if (!npcInterestStarted || npcInterestExpired || target is EntityPlayer) return;
        npcInterestUsedSeconds += Math.Max(0, dt);
        npcInterestSaveSeconds -= Math.Max(0, dt);
        if (npcInterestUsedSeconds >= C.NpcTraderInterestSeconds)
        {
            npcInterestUsedSeconds = C.NpcTraderInterestSeconds;
            npcInterestExpired = true;
            SaveNpcInterest();
            // Expired NPC interest means giving up the chase immediately, without
            // walking to the NPC's last location and without reacquiring another.
            if (IsNpcOrTrader(target))
            {
                target = null;
                lastKnownPreyPosition = null;
                lostPreyUntilMs = 0;
                StopNavigation();
                ResetProgress();
            }
        }
        else if (npcInterestSaveSeconds <= 0)
        {
            npcInterestSaveSeconds = 10;
            SaveNpcInterest();
        }
    }

    // Deliberately simple server-side decision loop. A normal VS waypoint
    // traverser handles walking; only one route request is issued per interval.
    public override void OnGameTick(float dt)
    {
        if (Api?.Side == EnumAppSide.Server && path != null && Alive && !IsDormant && TickPalisadeTrap(dt))
        {
            base.OnGameTick(dt);
            if (Alive) HoldPalisadeMovement();
            return;
        }
        base.OnGameTick(dt);
        if (Api?.Side != EnumAppSide.Server || path == null) return;
        using var tickTiming = system.Profiler.Measure("ai.entityTick");
        if (!Alive || IsDormant) { StopNavigation(); return; }
        if (TickVariant(dt)) return;

        path.OnGameTick(dt);
        attack -= dt;
        moan -= dt;
        doorCooldown -= dt;
        hurtSoundCooldown = Math.Max(0, hurtSoundCooldown - dt);
        long now = World.ElapsedMilliseconds;
        TickNpcInterest(dt);
        if (TickRotBait(now)) return;

        // Scan about once every 2 seconds rather than searching every tick.
        if (now >= nextPreyScanMs && system.AiBudget.TakeScan(now, C.AiPreyScansPerSecond))
        {
            system.Profiler.Count("ai.preyScans");
            nextPreyScanMs = now + (long)(C.AiPreyScanIntervalSeconds * 1000) + World.Rand.Next(250);
            Entity oldTarget = target;
            target = FindPrey();
            // A lost player (not a new NPC, not a different Alpha) is the only
            // reset event. If NPC interest was expired, reacquisition waits until
            // the next scheduled scan to avoid another expensive search now.
            if (oldTarget is EntityPlayer && target is not EntityPlayer)
                ResetNpcInterestAfterPlayerLost();
            if (IsNpcOrTrader(target)) StartNpcInterest();
            if (target != null)
            {
                alphaLeader = null;
                lostPreyUntilMs = 0;
                lastKnownPreyPosition = target.Pos.XYZ.Clone();
                idleDirection = HorizontalHeading(Pos.XYZ, target.Pos.XYZ, idleDirection);
                if (!ReferenceEquals(oldTarget, target))
                {
                    StopNavigation();
                    ResetProgress();
                    nextRouteMs = now + World.Rand.Next(250);
                    PlayMoan();
                    moan = RandomMoanDelay();
                }
            }
            else if (oldTarget != null)
            {
                RememberLostPrey(lastKnownPreyPosition ?? oldTarget.Pos.XYZ, now);
                StopNavigation();
                ResetProgress();
                nextFollowScanMs = nextIdleRouteMs = now + World.Rand.Next(250);
                roamPaused = false; roamPhaseUntil = 0;
            }
        }

        if (target != null && system.CanZombieTarget(target) && target.Pos.Dimension == Pos.Dimension)
        {
            AdvanceIdleTimer(dt, true);
            ChasePrey(dt, now);
            return;
        }
        if (target != null)
        {
            if (target is EntityPlayer) ResetNpcInterestAfterPlayerLost();
            RememberLostPrey(lastKnownPreyPosition ?? target.Pos.XYZ, now);
            StopNavigation();
            ResetProgress();
            roamPaused = false; roamPhaseUntil = 0;
        }
        target = null;
        // Standalone: keep idle zombies alive only near Survival players, NOT
        // because somebody occupies a 96x96 Last Watch zone. Player positions
        // are cached server-wide and checked at a configurable low frequency.
        if (C.ZombieStayActiveNearPlayers && now >= nextPresenceCheckMs)
        {
            nearbySurvivalPlayer = system.HasNearbySurvivalPlayer(new Vec3d(Pos.X, Pos.Y, Pos.Z),
                Pos.Dimension, C.ZombieKeepActiveRadiusBlocks);
            nextPresenceCheckMs = now + (long)(C.ZombiePresenceCheckSeconds * 1000);
        }
        if (AdvanceIdleTimer(dt, C.ZombieStayActiveNearPlayers && nearbySurvivalPlayer))
        {
            StopNavigation();
            Die(EnumDespawnReason.Expire);
            return;
        }
        if (FollowLastKnownPrey(now)) return;
        if (!FollowAlpha(now)) IdleWalk(now);
    }

    void RememberLostPrey(Vec3d lastPosition, long now)
    {
        lastKnownPreyPosition = lastPosition.Clone();
        idleDirection = HorizontalHeading(Pos.XYZ, lastPosition, idleDirection);
        lostPreyUntilMs = now + (long)(C.ZombieLostTargetPursuitSeconds * 1000);
        nextLostPreyRouteMs = now;
    }

    // A single bounded last-seen destination, not a breadcrumb graph.
    // Do not damage blocks when searching for prey that is no longer present.
    bool FollowLastKnownPrey(long now)
    {
        if (lastKnownPreyPosition == null || now >= lostPreyUntilMs ||
            Pos.SquareDistanceTo(lastKnownPreyPosition) <= 4)
        {
            if (lostPreyUntilMs != 0)
            {
                lostPreyUntilMs = 0;
                lastKnownPreyPosition = null;
                StopNavigation();
                nextFollowScanMs = nextIdleRouteMs = now;
            }
            return false;
        }
        if (now >= nextLostPreyRouteMs)
        {
            nextLostPreyRouteMs = RequestRoute(lastKnownPreyPosition.Clone(),
                C.MoveSpeed * VariantSpeedMultiplier, 1.5f,
                false, 800)
                ? lostPreyUntilMs : RetryAt(now);
        }
        SetWalking(path.Active);
        return true;
    }

    bool AdvanceIdleTimer(float dt, bool hasTarget)
    {
        idleDespawnSeconds = hasTarget ? 0 : idleDespawnSeconds + Math.Max(0, dt);
        return !hasTarget && idleDespawnSeconds >= C.ZombieIdleDespawnSeconds;
    }

    Entity FindPrey()
    {
        float range = system.DetectionRange(IsAlpha);
        // Usually ONE nearest-entity scan. Only make a second query when an
        // eligible NPC or other non-player is actually nearer than the player;
        // players have priority if detected, so NPC interest can be renewed
        // after a player chase ends.
        Entity nearest = World.GetNearestEntity(Pos.XYZ, range, range, e =>
            e != this && system.CanZombieTarget(e) &&
            e.Pos.Dimension == Pos.Dimension &&
            Pos.SquareDistanceTo(e.Pos.XYZ) <= range * range &&
            (!npcInterestExpired || !IsNpcOrTrader(e)) &&
            (e is not EntityPlayer player || !IsCrouched(player) ||
             Rules.CanAcquireSneakingPlayer(Pos.SquareDistanceTo(e.Pos.XYZ), range,
                 World.Rand.NextDouble(), C)));
        if (nearest == null || nearest is EntityPlayer) return nearest;
        Entity detectedPlayer = World.GetNearestEntity(Pos.XYZ, range, range, e =>
            e is EntityPlayer player && system.CanZombieTarget(player) &&
            player.Pos.Dimension == Pos.Dimension &&
            Pos.SquareDistanceTo(player.Pos.XYZ) <= range * range &&
            (!IsCrouched(player) || Rules.CanAcquireSneakingPlayer(
                Pos.SquareDistanceTo(player.Pos.XYZ), range, World.Rand.NextDouble(), C)));
        return detectedPlayer ?? nearest;
    }

    // A cheap, rate-limited direct walk follows moving prey in clear open space.
    // We only spend a full async A* search when a wall, elevation or distance
    // prevents direct pursuit. Never write to player controls or movement.
    bool CanDirectPursue(Vec3d destination)
    {
        double dx = destination.X - Pos.X, dz = destination.Z - Pos.Z;
        double range = C.PursuitDirectWalkRangeBlocks;
        double distanceSquared = dx * dx + dz * dz;
        if (distanceSquared > range * range || Math.Abs(destination.Y - Pos.Y) > 1.4)
            return false;
        double distance = Math.Sqrt(distanceSquared);
        int samples = (int)Math.Ceiling(distance / .7);
        // Check collision at torso height, excluding both occupied endpoints.
        for (int i = 1; i < samples; i++)
        {
            double t = (double)i / samples;
            var blockPos = new BlockPos(
                (int)Math.Floor(Pos.X + dx * t),
                (int)Math.Floor(Pos.Y + .9 + (destination.Y - Pos.Y) * t),
                (int)Math.Floor(Pos.Z + dz * t), Pos.Dimension);
            var block = World.BlockAccessor.GetBlock(blockPos);
            if (block.GetCollisionBoxes(World.BlockAccessor, blockPos)?.Length > 0)
                return false;
        }
        return true;
    }

    bool DirectPursuit(long now, Vec3d destination, float speed)
    {
        if (directPursuitActive && now < nextDirectSteerMs) return true;
        if (now < nextDirectSteerMs) return false;
        if (!CanDirectPursue(destination))
        {
            if (directPursuitActive) { StopNavigation(); nextRouteMs = now; }
            nextDirectSteerMs = now + (long)(C.PursuitSteerIntervalSeconds * 1000);
            return false;
        }
        if (!directPursuitActive) StopNavigation(); // retire any old asynchronous route
        long request = ++routeEpoch;
        directPursuitActive = true;
        routePending = false;
        bool accepted = path.WalkTowards(destination, speed, 1.1f,
            () => { if (routeEpoch == request) directPursuitActive = false; },
            () => { if (routeEpoch == request) { directPursuitActive = false; blockedRoute = true; nextRouteMs = now; } });
        if (!accepted && routeEpoch == request) directPursuitActive = false;
        nextDirectSteerMs = now + (long)(C.PursuitSteerIntervalSeconds * 1000);
        return accepted;
    }

    internal static bool HasTargetMoved(Vec3d previous, Vec3d current, double threshold)
    {
        if (previous == null) return true;
        double dx = previous.X - current.X, dz = previous.Z - current.Z;
        return dx * dx + dz * dz >= threshold * threshold;
    }

    // Navigation and collision continue to be handled by the vanilla game.
    void ChasePrey(float dt, long now)
    {
        bool inMeleeReach = Pos.SquareDistanceTo(target.Pos.XYZ) < 2.4 && ClearAttackLine(target);
        float speed = C.MoveSpeed * VariantSpeedMultiplier;
        Vec3d destination = target.Pos.XYZ.Clone();
        bool direct = !inMeleeReach && DirectPursuit(now, destination, speed);
        if (!inMeleeReach && !direct && now >= nextRouteMs)
        {
            bool moved = HasTargetMoved(lastChaseRouteDestination, destination,
                C.PursuitRepathTargetMovementBlocks);
            // Stay on the existing waypoint route if prey hasn't changed direction.
            // A pending path is retired only when it is stale *and* prey has moved.
            if (routePending && moved && now - pendingRouteSince >= 1500) StopNavigation();
            if (!routePending && (moved || !path.Active || blockedRoute))
            {
                bool requested = RequestRoute(destination, speed, 1.1f, true, 1500);
                nextRouteMs = requested
                    ? now + (long)(C.SiegeNavigationRefreshSeconds * 1000) + World.Rand.Next(250)
                    : RetryAt(now);
                if (requested) lastChaseRouteDestination = destination;
            }
            else nextRouteMs = now + (long)(C.SiegeNavigationRefreshSeconds * 1000);
        }

        // No breadcrumb memory, ladders, searching stages or custom route graphs.
        // Only check if the zombie has physically moved, and only sample every
        // few seconds. A stuck zombie may break a wall *between it and prey*.
        if (C.SiegeEnabled && C.BlockBreakingEnabled && !inMeleeReach &&
            target != null && target.Alive && (path.Active || blockedRoute || routePending))
        {
            progressSampleSeconds += dt;
            if (progressSampleSeconds >= C.SiegeProgressSampleSeconds)
            {
                double moved = lastProgressPosition == null ? double.MaxValue :
                    Math.Sqrt(Pos.SquareDistanceTo(lastProgressPosition));
                stationarySeconds = moved < C.SiegeMinimumTravelBlocks
                    ? stationarySeconds + progressSampleSeconds : 0;
                lastProgressPosition = Pos.XYZ.Clone();
                progressSampleSeconds = 0;
                if (moved >= C.SiegeMinimumTravelBlocks) blockedRoute = false;
            }
            if (stationarySeconds >= C.SiegeStuckSeconds ||
                (blockedRoute && stationarySeconds >= C.SiegeProgressSampleSeconds))
            {
                breakTimer -= dt;
                if (breakTimer <= 0)
                {
                    breakTimer = C.BlockBreakIntervalSeconds;
                    // The obstruction search checks the ray toward the target,
                    // block reach, land claims, tile entities and reinforcement.
                    if (World.Rand.NextDouble() <
                        (system.Horde ? C.HordeBlockBreakChance : C.BlockBreakChance) &&
                        TryBreakObstructingBlock())
                    {
                        nextRouteMs = now + World.Rand.Next(250);
                        ResetProgress();
                    }
                }
            }
        }

        if (moan <= 0) { PlayMoan(); moan = RandomMoanDelay(); }
        SetWalking(path.Active);
        if (inMeleeReach)
        {
            StopNavigation();
            SetWalking(false);
            if (attack <= 0)
            {
                attack = C.AttackCooldownSeconds;
                AnimManager.StartAnimation("attack");
                var health = target.GetBehavior<EntityBehaviorHealth>();
                float before = health?.Health ?? 0;
                bool received = target.ReceiveDamage(new DamageSource {
                    Source = EnumDamageSource.Entity, SourceEntity = this,
                    Type = EnumDamageType.BluntAttack
                }, C.AttackDamage * (IsAlpha ? C.AlphaAttackDamageMultiplier : 1f));
                if (received && target is EntityPlayer playerTarget && playerTarget.Alive &&
                    health != null && health.Health < before)
                    system.Hit(playerTarget, IsAlpha);
            }
        }
        if (system.Horde && C.HordeOpensDoors && doorCooldown <= 0)
        {
            doorCooldown = C.HordeDoorAttemptCooldownSeconds;
            TryOpenDoorAhead();
        }
    }

    void ResetProgress()
    {
        lastProgressPosition = Pos.XYZ.Clone();
        stationarySeconds = progressSampleSeconds = 0;
        blockedRoute = false;
        breakTimer = C.BlockBreakIntervalSeconds;
    }

    // Never use an Alpha as an attack target. Its movement is only a waypoint.
    bool FollowAlpha(long now)
    {
        if (IsAlpha || !C.FollowAlphaEnabled) { alphaLeader = null; return false; }
        if (alphaLeader != null && (!alphaLeader.Alive || alphaLeader.IsDormant ||
            alphaLeader.Pos.Dimension != Pos.Dimension ||
            Pos.SquareDistanceTo(alphaLeader.Pos.XYZ) > C.AlphaFollowRange * C.AlphaFollowRange))
        {
            alphaLeader = null;
            StopNavigation();
            nextIdleRouteMs = 0;
            roamPaused = false; roamPhaseUntil = 0;
        }
        if (now >= nextFollowScanMs)
        {
            nextFollowScanMs = now + (long)(C.AlphaFollowRefreshSeconds * 1000);
            if (alphaLeader == null && !system.AiBudget.TakeScan(now, C.AiPreyScansPerSecond))
            { nextFollowScanMs = RetryAt(now); return false; }
            alphaLeader ??= World.GetNearestEntity(Pos.XYZ, C.AlphaFollowRange, C.AlphaFollowRange,
                e => e is Zombie z && z.IsAlpha && z.Alive && !z.IsDormant &&
                z.Pos.Dimension == Pos.Dimension) as Zombie;
            if (alphaLeader != null)
            {
                idleDirection = HorizontalHeading(Pos.XYZ, alphaLeader.Pos.XYZ, idleDirection);
                if (Pos.SquareDistanceTo(alphaLeader.Pos.XYZ) >
                    C.AlphaFollowStopDistance * C.AlphaFollowStopDistance)
                {
                    if (!RequestRoute(alphaLeader.Pos.XYZ.Clone(), C.MoveSpeed,
                        C.AlphaFollowStopDistance, false, 1500)) nextFollowScanMs = RetryAt(now);
                }
                else StopNavigation();
            }
        }
        if (alphaLeader == null) return false;
        SetWalking(path.Active);
        return true;
    }

    // Slow forward idle walk, favoring the heading of the previous target.
    // If this zombie has never seen prey, its first heading is random.
    void IdleWalk(long now)
    {
        if (!C.RoamEnabled) { StopNavigation(); SetWalking(false); return; }
        if (roamPhaseUntil == 0) roamPhaseUntil = now + (long)(C.RoamWalkSeconds * 1000);
        if (now >= roamPhaseUntil)
        {
            roamPaused = !roamPaused;
            roamPhaseUntil = now + (long)((roamPaused ? C.RoamPauseSeconds : C.RoamWalkSeconds) * 1000);
            StopNavigation();
            if (!roamPaused) { idleDirection = RandomHorizontalDirection(); nextIdleRouteMs = now; }
        }
        if (roamPaused) { SetWalking(false); return; }
        if (now >= nextIdleRouteMs)
        {
            idleDirection ??= RandomHorizontalDirection();
            Vec3d destination = Pos.XYZ.AddCopy(idleDirection.X * 6, 0, idleDirection.Z * 6);
            nextIdleRouteMs = RequestRoute(destination,
                C.MoveSpeed * VariantSpeedMultiplier * C.RoamSpeedMultiplier,
                1.1f, false, 1200)
                ? now + (long)(C.RoamRouteRefreshSeconds * 1000) + World.Rand.Next(500)
                : RetryAt(now);
        }
        SetWalking(path.Active);
    }

    void SetWalking(bool walking)
    {
        if (walkingAnimation == walking) return;
        walkingAnimation = walking;
        if (walking) AnimManager.StartAnimation("walk");
        else AnimManager.StopAnimation("walk");
    }

    static Vec3d HorizontalHeading(Vec3d from, Vec3d toward, Vec3d fallback)
    {
        double x = toward.X - from.X, z = toward.Z - from.Z;
        double distance = Math.Sqrt(x * x + z * z);
        return distance < .1 ? fallback : new Vec3d(x / distance, 0, z / distance);
    }

    Vec3d RandomHorizontalDirection()
    {
        double angle = World.Rand.NextDouble() * Math.PI * 2;
        return new Vec3d(Math.Cos(angle), 0, Math.Sin(angle));
    }

    static bool IsCrouched(EntityPlayer player) => player.Controls?.Sneak == true;

    bool ClearAttackLine(Entity p)
    {
        // BlockPos carries the dimension separately, so use local Y here.
        var start = new Vec3d(Pos.X, Pos.Y + .9, Pos.Z);
        var end = new Vec3d(p.Pos.X, p.Pos.Y + .9, p.Pos.Z);
        for (int i = 1; i < 10; i++)
        {
            double t = i / 10.0;
            var pos = new BlockPos((int)Math.Floor(start.X + (end.X-start.X)*t), (int)Math.Floor(start.Y + (end.Y-start.Y)*t), (int)Math.Floor(start.Z + (end.Z-start.Z)*t), Pos.Dimension);
            var block = World.BlockAccessor.GetBlock(pos);
            if (block.GetCollisionBoxes(World.BlockAccessor, pos)?.Length > 0) return false;
        }
        return true;
    }
    float RandomMoanDelay() => C.MoanMinSeconds +
        (float)World.Rand.NextDouble() * (C.MoanMaxSeconds - C.MoanMinSeconds);

    void PlayMoan()
    {
        if (!Alive || Api?.Side != EnumAppSide.Server) return;
        World.PlaySoundAt(new AssetLocation("seventhnightzombies:sounds/moan" + (1 + World.Rand.Next(3))),
            this, null, C.ZombieMoanVolume, 28, .65f);
    }

    // The system calls this once when a timed or manual horde starts. Zombies then
    // stay quiet unless they acquire a player or remain actively engaged.
    public void OnHordeStarted()
    {
        if (!Alive || Api?.Side != EnumAppSide.Server) return;
        PlayMoan();
        moan = RandomMoanDelay();
    }

    // Probe the first obstruction along a HORIZONTAL approach at torso height.
    // Unlike the old ray toward a high target, this cannot dig through ceilings/floors.
    BlockPos FindSiegeObstruction()
    {
        if (target == null || !system.CanZombieTarget(target) || target.Pos.Dimension != Pos.Dimension) return null;
        var from = Pos.XYZ.AddCopy(0, .9, 0);
        var to = new Vec3d(target.Pos.X, from.Y, target.Pos.Z);
        double dx = to.X - from.X, dz = to.Z - from.Z;
        double distance = Math.Sqrt(dx * dx + dz * dz);
        if (distance <= 1.1) return null;
        double end = Math.Min(C.BlockBreakReach, distance - .3);
        var seen = new HashSet<string>();
        for (double travel = .4; travel <= end; travel += .2)
        {
            double x = from.X + dx * travel / distance, z = from.Z + dz * travel / distance;
            // Two-block-high openings: break the feet-level obstruction first,
            // then the head-level obstruction on a later strike if necessary.
            for (int height = 0; height <= 1; height++)
            {
                var pos = new BlockPos((int)Math.Floor(x), (int)Math.Floor(Pos.Y) + height,
                    (int)Math.Floor(z), Pos.Dimension);
                if (!seen.Add($"{pos.X}:{pos.Y}:{pos.Z}")) continue;
                if (World.BlockAccessor.GetChunkAtBlockPos(pos) == null) return null;
                var block = World.BlockAccessor.GetBlock(pos);
                if (block.Id == 0 || block.GetCollisionBoxes(World.BlockAccessor, pos)?.Length is not > 0) continue;
                // A protected block stops the siege attempt. Never break through
                // another player's land claim or a block entity behind a wall.
                return IsSafeToSiege(pos, block) ? pos : null;
            }
        }
        return null;
    }

    bool IsSafeToSiege(BlockPos pos, Block block)
    {
        if (C.ProtectClaims && (World.Claims.Get(pos)?.Length ?? 0) > 0) return false;
        if (World.BlockAccessor.GetBlockEntity(pos) != null) return false;
        string code = block.Code?.Path?.ToLowerInvariant() ?? "";
        if (code.Contains("bedrock") || code.Contains("mantle") || code.Contains("barrier") ||
            code.Contains("admin") || code.Contains("creative") || code.Contains("unbreakable") ||
            code.Contains("reinforcement") || code.Contains("portal")) return false;
        return block.Replaceable <= 2000;
    }

    // Returns true only when this attack fully removed a block, permitting a re-path.
    bool TryBreakObstructingBlock()
    {
        var pos = FindSiegeObstruction();
        if (pos == null) return false;
        return TryDamageWallBlock(pos, World.BlockAccessor.GetBlock(pos));
    }

    bool TryDamageWallBlock(BlockPos pos, Block block)
    {
        // Never damage claimed land, unbreakable/admin blocks, or blocks with
        // block entities (inventories, doors, machinery and other stored data).
        if (!IsSafeToSiege(pos, block)) return false;

        // This switch consistently protects reinforced walls AND doors.
        // Test here before any reinforcement mutation or block damage.
        if (C.ProtectReinforcedBlocks &&
            Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.IsReinforced(pos) == true)
            return false;

        var reinforcement = Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
        if (reinforcement != null)
        {
            var data = reinforcement.GetReinforcment(pos);
            if (data != null && data.Strength > 0)
            {
                // During and outside hordes use independent damage settings.
                // Never change owner/group identity or bypass claim protection.
                int strikeDamage = system.Horde
                    ? C.HordeBlockReinforcementDamage
                    : C.NonHordeBlockReinforcementDamage;
                int newStrength = data.Strength - strikeDamage;
                if (newStrength > 0)
                {
                    var owner = !string.IsNullOrEmpty(data.PlayerUID)
                        ? World.PlayerByUid(data.PlayerUID)
                        : null;
                    owner ??= World.AllPlayers.FirstOrDefault(p => p.PlayerName == data.LastPlayername);
                    // If owner data cannot be restored, fail closed: a zombie
                    // must not strip somebody else's reinforcement ownership.
                    if (owner == null) return false;
                    try
                    {
                        reinforcement.ClearReinforcement(pos);
                        reinforcement.StrengthenBlock(pos, owner, newStrength, data.GroupUid);
                    }
                    catch (Exception ex)
                    {
                        Api.Logger.Warning("Seventh Night Zombies: unable to adjust reinforcement at " + pos + ": " + ex.Message);
                        // ClearReinforcement may have succeeded before the error.
                        // Best-effort restore the previous ownership and strength;
                        // never continue to breaking a block after this failure.
                        try
                        {
                            if (!reinforcement.IsReinforced(pos))
                                reinforcement.StrengthenBlock(pos, owner, data.Strength, data.GroupUid);
                        }
                        catch (Exception restoreEx)
                        {
                            Api.Logger.Error("Seventh Night Zombies: reinforcement restoration failed at " + pos + ": " + restoreEx.Message);
                        }
                    }
                    return false;
                }
                reinforcement.ClearReinforcement(pos);
                // Consumes the FINAL strength point now. A future successful
                // attack can break the block, never this same hit.
                return false;
            }
        }
        // Vanilla BreakBlock applies ordinary block drops/neighbor updates.
        World.BlockAccessor.BreakBlock(pos, null);
        // No player break event fires for a zombie strike: tell the zone
        // controller so stronghold/bell/path records do not outlive the block.
        (system.WorldDirector as LastWatchZones)?.ForgetBlock(pos, block);
        return true;
    }

    // Horde-only, server-side opening; never destroys or replaces blocks.
    void TryOpenDoorAhead()
    {
        if (target == null || !target.Alive) return;
        var origin = Pos.AsBlockPos;
        // Scan adjacent door cells, prioritizing cells in the direction of the target.
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                if (dx * (target.Pos.X - Pos.X) + dz * (target.Pos.Z - Pos.Z) <= 0) continue;
                for (int height = 0; height <= 1; height++)
                {
                    var pos = new BlockPos(origin.X + dx, origin.Y + height, origin.Z + dz, Pos.Dimension);
                    if (World.BlockAccessor.GetChunkAtBlockPos(pos) == null) continue;
                    if (C.ProtectClaims && (World.Claims.Get(pos)?.Length ?? 0) > 0) continue;
                    if (C.ProtectReinforcedBlocks &&
                        Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.IsReinforced(pos) == true) continue;
                    var door = World.BlockAccessor.GetBlock(pos).GetBEBehavior<BEBehaviorDoor>(pos);
                    if (door == null || door.Opened) continue;
                    if (target is not EntityPlayer playerTarget || playerTarget.Player == null) continue;
                    door.ToggleDoorState(playerTarget.Player, true);
                    nextRouteMs = 0;
                    return;
                }
            }
    }

    public override void OnEntityDespawn(EntityDespawnData reason)
    {
        path?.Stop();
        if (Api?.Side == EnumAppSide.Server)
        {
            system?.UntrackActiveZombie(this);
            system?.Population?.OnDespawn(this, reason);
        }
        base.OnEntityDespawn(reason);
    }
}
