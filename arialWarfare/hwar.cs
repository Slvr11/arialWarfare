using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using InfinityScript;
using static InfinityScript.GSCFunctions;

public class hwar : BaseScript
{
    private static List<Entity> helis = new List<Entity>();
    public hwar()
    {
        precacheGametype();

        MakeDvarServerInfo("ui_gametype", "Arial Warfare");
        MakeDvarServerInfo("sv_gametypeName", "Arial Warfare");
        //-Enable turning anims on players-
        //SetDvar("player_turnAnims", 1);
        //Set high quality voice chat audio
        SetDvar("sv_voiceQuality", 9);
        SetDvar("maxVoicePacketsPerSec", 2000);
        SetDvar("maxVoicePacketsPerSecForServer", 1000);
        //Ensure all players are heard regardless of any settings
        SetDvar("cg_everyoneHearsEveryone", 1);
        AfterDelay(100, () => SetDynamicDvar("scr_game_hardpoints", 0));

        PlayerConnected += onPlayerConnect;

        spawnNormalHelicopter(new Vector3(44, 1041, -280), new Vector3(0, -157, 0));
    }

    private static void precacheGametype()
    {
        PreCacheShader("viper_ammo_overlay_mp");
        PreCacheShader("viper_missile_overlay_mp");
    }

    public override void OnSay(Entity player, string name, string message)
    {
        if (message == "viewpos")
        {
            InfinityScript.Log.Write(LogLevel.Info, "({0}, {1}, {2})", player.Origin.X, player.Origin.Y, player.Origin.Z);
            Vector3 angles = player.GetPlayerAngles();
            InfinityScript.Log.Write(LogLevel.Info, "({0}, {1}, {2})", angles.X, angles.Y, angles.Z);
        }
    }

    private static void onPlayerConnect(Entity player)
    {
        player.SetField("isPilotingVehicle", false);
        player.SetField("vehicle", player);
        player.SetField("isNearHeli", false);
        player.SetField("currentActiveHeli", player);

        player.SetClientDvar("camera_thirdPerson", 0);

        //If we want to spawn the player with no weapons
        //player.OnNotify("giveLoadout", (p) => player.TakeAllWeapons());
        player.NotifyOnPlayerCommand("use_button_pressed", "+activate");
        player.OnNotify("use_button_pressed", checkForHeliPilot);
        player.NotifyOnPlayerCommand("heli_missile_fire", "+toggleads_throw");
        player.NotifyOnPlayerCommand("heli_missile_fire", "+speed_throw");

        player.SpawnedPlayer += () => onPlayerSpawned(player);
    }
    private static void onPlayerSpawned(Entity player)
    {
        OnInterval(100, () => heliHintTracker(player));
    }

    private static bool heliHintTracker(Entity player)
    {
        if (player.GetField<bool>("isNearHeli") || player.GetField<bool>("isPilotingVehicle"))
        {
            if (!player.IsAlive || player.Classname != "player") return false;
            return true;
        }

        foreach (Entity heli in helis)
        {
            if (heli.GetTagOrigin("tag_gunner").DistanceTo(player.Origin) > 128 || heli.GetField<bool>("isBeingPiloted"))
                continue;

            player.ForceUseHintOn("Press ^3[{+activate}]^7 to pilot");
            player.SetField("isNearHeli", true);
            player.SetField("currentActiveHeli", heli);
            OnInterval(100, () => heliDistanceTracker(player));
            break;
        }

        if (!player.IsAlive || player.Classname != "player") return false;
        return true;
    }
    private static bool heliDistanceTracker(Entity player)
    {
        if (!player.GetField<bool>("isNearHeli") || player.GetField<Entity>("currentActiveHeli") == player)
            return false;

        Entity heli = player.GetField<Entity>("currentActiveHeli");

        if (heli.GetTagOrigin("tag_gunner").DistanceTo(player.Origin) > 128 || heli.GetField<bool>("isBeingPiloted"))
        {
            player.ForceUseHintOff();
            player.SetField("isNearHeli", false);
            player.SetField("currentActiveHeli", player);
            heliHintTracker(player);
            return false;
        }

        if (!player.IsAlive || player.Classname != "player" || !player.GetField<bool>("isNearHeli")) return false;
        return true;
    }
    private static void checkForHeliPilot(Entity player)
    {
        if (player.SessionTeam == "spectator") return;

        if (!player.GetField<bool>("isPilotingVehicle"))
        {
            if (player.GetField<Entity>("currentActiveHeli") == player) return;
            Entity heliVisual = player.GetField<Entity>("currentActiveHeli");

            if (heliVisual.GetField<bool>("isBeingPiloted")) return;

            Entity heli = turnHelicopterOn(player, heliVisual);
            heliVisual.SetField("helicopter", heli);
            heliVisual.SetField("isBeingPiloted", true);
            StartAsync(pilotHelicopter(player, heli));
        }
        else if (player.GetField<bool>("isPilotingVehicle"))
        {
            //Entity currentHeli = player.GetField<Entity>("vehicle");
            //if (currentHeli == player) return;

            exitHelicopter(player);
        }
    }

    private static IEnumerator pilotHelicopter(Entity player, Entity heli)
    {
        if (!player.IsAlive || player.Classname != "player") yield break;

        player.ForceUseHintOff();
        player.SetField("isPilotingVehicle", true);
        player.SetField("vehicle", heli);

        player.DisableWeapons();
        player.FreezeControls(true);
        Entity cam = Spawn("script_model", player.Origin);
        cam.Angles = player.GetPlayerAngles();
        cam.SetModel("tag_origin");
        player.PlayerLinkToAbsolute(cam, "tag_origin");
        Vector3 camPos = heli.GetTagOrigin("tag_gunner") - new Vector3(0, 0, 40);
        cam.MoveTo(camPos, 3, .5f, .5f);
        cam.RotateTo(heli.Angles, 3, .5f, .5f);

        yield return Wait(3);

        player.Unlink();
        player.FreezeControls(false);
        player.SetOrigin(heli.GetTagOrigin("tag_gunner"));
        player.SetStance("crouch");
        //player.FreezeControls(true);
        player.PlayerLinkToAbsolute(heli, "tag_gunner");
        //cam.LinkTo(player, "tag_origin", new Vector3(100, 100, 100), Vector3.Zero);
        player.CameraUnlink();
        cam.Delete();
        player.ControlsLinkTo(heli);
        //player.CameraLinkTo(heli);
        player.SetClientDvar("camera_thirdPerson", 1);
        player.SetClientDvar("camera_thirdPersonOffset", new Vector3(-550, 70, 50));

        OnInterval(10000, () => heli_restockMissiles(heli));
        OnInterval(50, () => heli_watchFiring(heli));
        StartAsync(heli_watchMissileFire(heli));
    }
    private static void exitHelicopter(Entity player)
    {
        Entity heli = player.GetField<Entity>("vehicle");
        Vector3 left = AnglesToRight(heli.Angles) * -25;
        Vector3 exitPos = heli.GetTagOrigin("tag_gunner") + left;

        player.ControlsUnlink();
        player.Unlink();
        //player.CameraUnlink();
        player.SetClientDvar("camera_thirdPerson", 0);
        player.EnableWeapons();
        player.SetOrigin(exitPos);
        player.SetStance("stand");
        player.FreezeControls(false);
        player.SetField("vehicle", player);
        player.SetField("isPilotingVehicle", false);

        StartAsync(landHelicopter(heli));
        AfterDelay(100, () => heli.ClearField("pilot"));
    }

    private static bool heli_restockMissiles(Entity heli)
    {
        if (!heli.HasField("pilot")) return false;
        if (heli.GetField<int>("missiles") > 4)
            return true;

        heli.SetField("missiles", heli.GetField<int>("missiles") + 1);
        return true;
    }
    private static bool heli_watchFiring(Entity heli)
    {
        if (!heli.HasField("pilot")) return false;

        Entity player = heli.GetField<Entity>("pilot");
        int ammo = heli.GetField<int>("ammo");
        bool primaryFireButton = player.AttackButtonPressed();

        //Utilities.PrintToConsole(secondaryFireButton.ToString());

        if (primaryFireButton && ammo > 0)
        {
            heli.FireWeapon("tag_flash");
            heli.PlaySound("blackhawk_minigun_gatling_fire");//Temp
            heli.SetField("ammo", heli.GetField<int>("ammo") - 1);

            if (heli.GetField<int>("ammo") == 0)
            {
                //reload
            }
        }

        if(!heli.HasField("pilot") || !player.IsAlive || player.Classname != "player" || heli.GetField<int>("damageTaken") >= heli.Health) return false;
        return true;
    }
    private static IEnumerator heli_watchMissileFire(Entity heli)
    {
        if (!heli.HasField("pilot"))
            yield break;

        Entity pilot = heli.GetField<Entity>("pilot");

        yield return pilot.WaitTill("heli_missile_fire");

        if (!heli.HasField("pilot"))//Check again, we may have already left the heli
            yield break;

        int missiles = heli.GetField<int>("missiles");
        int lastMissileTime = heli.GetField<int>("lastMissileFire");

        if (missiles > 0 && GetTime() > lastMissileTime + 5000)
        {
            Utilities.PrintToConsole("Firing missile");
            Vector3 forward = AnglesToForward(heli.Angles);
            Vector3 target = heli.GetTagOrigin("tag_flash") + (forward * 50);
            MagicBullet("harrier_missile_mp", heli.GetTagOrigin("tag_flash"), target, pilot);
            heli.PlaySound("weap_cobra_missile_fire");
            heli.SetField("missiles", heli.GetField<int>("missiles") - 1);
            heli.SetField("lastMissileFire", GetTime());
            StartAsync(heli_watchMissileFire(heli));
        }
        else
        {
            StartAsync(heli_watchMissileFire(heli));
        }
    }

    private static IEnumerator landHelicopter(Entity heli)
    {
        Vector3 ground;

        if (heli.HasField("pilot"))
        {
            Entity owner = heli.GetField<Entity>("pilot");
            while (!owner.IsOnGround())
                yield return 0;

            ground = new Vector3(heli.Origin.X, heli.Origin.Y, owner.Origin.Z);
        }
        else
            ground = PhysicsTrace(heli.Origin, heli.Origin - new Vector3(0, 0, 2000));

        ground += new Vector3(0, 0, 180);
        heli.SetVehGoalPos(ground, true);

        //Utilities.PrintToConsole(heli.Origin.DistanceTo(ground).ToString());

        while (heli.Origin.DistanceTo(ground) > 10)
        {
            //Utilities.PrintToConsole(heli.Origin.DistanceTo(ground).ToString());
            yield return 0;
        }

        turnHelicopterOff(heli.GetField<Entity>("visual"));
    }

    private static Entity spawnNormalHelicopter(Vector3 pathStart, Vector3 forward)
    {
        Entity visual = Spawn("script_model", pathStart + new Vector3(0, 0, 180));
        visual.SetModel("vehicle_cobra_helicopter_fly_low");
        visual.Angles = forward;
        visual.MakeVehicleSolidSphere(96, -128);
        visual.SetField("isBeingPiloted", false);
        helis.Add(visual);

        return visual;
    }
    private static Entity spawnNormalHelicopterForPlayer(Entity owner, Entity visual)
    {
        Vector3 pathStart = visual.Origin;
        Vector3 forward = visual.Angles;
        Entity heli = SpawnHelicopter(owner, pathStart, forward, "cobra_mp", "vehicle_cobra_helicopter_fly_low");
        if (heli == null) return null;

        heli.Health = 5000;
        heli.SetField("maxHealth", 5000);
        heli.SetField("damageTaken", 0);
        heli.SetField("ammo", 100);
        heli.SetField("missiles", 1);
        heli.SetField("lastMissileFire", GetTime());
        heli.SetField("pilot", owner);
        heli.SetField("visual", visual);

        heli.EnableLinkTo();
        heli.SetVehicleLookAtText("The Dickweed", "The Dickweed");
        //heli.MakeVehicleSolidSphere(96, -128);
        heli.SetSpeed(50, 20, 20);
        heli.SetHoverParams(20, 10, 5);
        heli.SetTurningAbility(.1f);
        heli.SetVehWeapon("cobra_20mm_mp");

        return heli;
    }

    private static void turnHelicopterOff(Entity heli)
    {
        heli.ShowAllParts();
        heli.Solid();
        heli.MakeVehicleSolidSphere(96, -128);
        heli.SetField("isBeingPiloted", false);
        heli.Origin = heli.GetField<Entity>("helicopter").Origin;
        heli.Angles = heli.GetField<Entity>("helicopter").Angles;
        heli.GetField<Entity>("helicopter").FreeHelicopter();
        heli.GetField<Entity>("helicopter").Delete();
        heli.ClearField("helicopter");
    }
    private static Entity turnHelicopterOn(Entity owner, Entity heli)
    {
        heli.HideAllParts();
        heli.NotSolid();
        heli.SetField("isBeingPiloted", true);
        return spawnNormalHelicopterForPlayer(owner, heli);
    }
}