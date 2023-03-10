using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RWCustom;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SlugcatEyebrowRaise
{
    internal static class Hooks
    {
        private static SoundID? GetVineBoomSoundID() => Options.vineBoomLoud.Value ? Enums.Sounds.VineBoomLoud : Enums.Sounds.VineBoom;
        private static string GetVineBoomStringID() => Options.vineBoomLoud.Value ? "VineBoomLoud" : "VineBoom";

        public static void ApplyHooks()
        {
            On.RainWorld.OnModsInit += RainWorld_OnModsInit;
            On.RainWorld.OnModsDisabled += RainWorld_OnModsDisabled;

            On.ModManager.RefreshModsLists += ModManager_RefreshModsLists;
            On.AssetManager.ResolveFilePath += AssetManager_ResolveFilePath;

            On.Player.Update += Player_Update;
            On.PlayerGraphics.DrawSprites += PlayerGraphics_DrawSprites;

            On.RoomCamera.DrawUpdate += RoomCamera_DrawUpdate;

            On.Menu.RainEffect.LightningSpike += RainEffect_LightningSpike;
        }

        private static bool isInit = false;

        private static void RainWorld_OnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self)
        {
            orig(self);

            if (isInit) return;
            isInit = true;

            MachineConnector.SetRegisteredOI(SlugcatEyebrowRaise.MOD_ID, Options.instance);
            Enums.Sounds.RegisterValues();
            ResourceLoader.LoadSprites();

            try
            {
                IL.Player.Die += Player_Die;
                IL.HUD.TextPrompt.Update += TextPrompt_Update;

                //IL.AssetManager.ResolveFilePath += AssetManager_ResolveFilePathIL;

            }
            catch (Exception ex)
            {
                SlugcatEyebrowRaise.Logger.LogError(ex);
            }
        }

        // Figure this out later
        //private static void AssetManager_ResolveFilePathIL(ILContext il)
        //{
        //    ILLabel? dest = null;

        //    var c = new ILCursor(il);
        //    while (c.TryGotoNext(MoveType.AfterLabel,
        //        i => i.MatchBr(out dest)
        //        ))
        //    {
        //        c.MoveAfterLabels();
        //        c.EmitDelegate<Func<int, bool>>((index) =>
        //        {
        //            return ModManager.ActiveMods[index].id == SlugcatEyebrowRaise.MOD_ID;
        //        });

        //        c.Emit(OpCodes.Brtrue, dest);

        //        break;
        //    }
        //}

        // Hacky and not recommended, should use an IL Hook instead but I am not experienced enough yet
        // Expecting to run into compatibility issues here!
        private static string AssetManager_ResolveFilePath(On.AssetManager.orig_ResolveFilePath orig, string path)
        {
            if (Options.disableGraphicsOverride.Value) return orig(path);

            string filePath = Path.Combine(modPath, path.ToLowerInvariant());

            if (Options.enableIllustrations.Value && File.Exists(filePath))
            {
                SlugcatEyebrowRaise.Logger.LogWarning($"Gave asset eyebrow (overrode it): {path}");
                return filePath;
            }

            //return orig(path);

            // Original Asset Manager code
            string text = Path.Combine(Path.Combine(Custom.RootFolderDirectory(), "mergedmods"), path.ToLowerInvariant());
            if (File.Exists(text))
            {
                return text;
            }
            for (int i = ModManager.ActiveMods.Count - 1; i >= 0; i--)
            {
                // Added
                if (ModManager.ActiveMods[i].id == SlugcatEyebrowRaise.MOD_ID) continue;

                string text2 = Path.Combine(ModManager.ActiveMods[i].path, path.ToLowerInvariant());
                if (File.Exists(text2))
                {
                    return text2;
                }
            }
            return Path.Combine(Custom.RootFolderDirectory(), path.ToLowerInvariant());
        }

        private static string modPath = "";

        private static void ModManager_RefreshModsLists(On.ModManager.orig_RefreshModsLists orig, RainWorld rainWorld)
        {
            orig(rainWorld);

            foreach (ModManager.Mod mod in ModManager.ActiveMods)
            {
                if (mod.id != SlugcatEyebrowRaise.MOD_ID) continue;

                modPath = mod.path;
                break;
            }
        }

        private static void RainWorld_OnModsDisabled(On.RainWorld.orig_OnModsDisabled orig, RainWorld self, ModManager.Mod[] newlyDisabledMods)
        {
            orig(self, newlyDisabledMods);

            Enums.Sounds.UnregisterValues();
        }

        private static void RainEffect_LightningSpike(On.Menu.RainEffect.orig_LightningSpike orig, Menu.RainEffect self, float newInt, float dropOffFrames)
        {
            orig(self, newInt, dropOffFrames);

            if (self.lightningIntensity > 0.3f) self.menu.PlaySound(GetVineBoomSoundID(), 0.0f, self.lightningIntensity + 0.2f, 0.5f);
        }

        private const int MAX_NUMBER_OF_PLAYERS = 4;

        private const float SHAKE_DURATION = 1.5f;
        private const float SHAKE_INTENSITY_NORMAL = 0.15f;
        private const float SHAKE_INTENSITY_LOUD = 1.0f;

        private readonly static bool[] isPlayerKeyPressed = new bool[MAX_NUMBER_OF_PLAYERS];
        private readonly static bool[] isPlayerEyebrowRaised = new bool[MAX_NUMBER_OF_PLAYERS];
        private readonly static int[] playerEyebrowRaiseLevel = new int[MAX_NUMBER_OF_PLAYERS];
        private readonly static float[] playerEyebrowRaiseDurationTimer = new float[MAX_NUMBER_OF_PLAYERS];
        private readonly static float[] raiseTimers = new float[MAX_NUMBER_OF_PLAYERS];

        private static float cameraZoomAmount = 0.0f;
        private static float shakeTimer = 0.0f;
        private static float zoomTimer = 0.0f;

        private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);

            HandlePlayerInput(self, Options.player1Keybind.Value, 0);
            HandlePlayerInput(self, Options.player2Keybind.Value, 1);
            HandlePlayerInput(self, Options.player3Keybind.Value, 2);
            HandlePlayerInput(self, Options.player4Keybind.Value, 3);

            if (zoomTimer < Time.time)
            {
                cameraZoomAmount = 0.0f;
            }
        }

        private static void HandlePlayerInput(Player player, KeyCode keyCode, int targetPlayerIndex)
        {
            int playerIndex = player.playerState.playerNumber;
            if (playerIndex != targetPlayerIndex) return;

            if (Input.GetKey(keyCode) || (playerIndex == 0 && Input.GetKey(Options.keyboardKeybind.Value)))
            {
                if (!isPlayerKeyPressed[playerIndex] || Options.playEveryFrame.Value)
                {
                    player.room.PlaySound(GetVineBoomSoundID(), player.mainBodyChunk, false, Options.eyebrowRaiseVolume.Value / 100.0f, 1.0f);
                    playerEyebrowRaiseDurationTimer[playerIndex] = Time.time + (Options.eyebrowRaiseDuration.Value / 10.0f);
                    EyebrowRaiseExplosion(player);

                    if (Options.cameraShake.Value)
                    {
                        shakeTimer = Time.time + SHAKE_DURATION;
                    }

                    if (Options.zoomCamera.Value && player.room.game.Players.Count == 1)
                    {
                        cameraZoomAmount = (Options.eyebrowRaiseZoom.Value / 100.0f);
                        zoomTimer = Time.time + (Options.eyebrowRaiseZoomDuration.Value / 10.0f);
                    }
                }

                isPlayerEyebrowRaised[playerIndex] = true;

                if (!player.isSlugpup)
                {
                    isPlayerKeyPressed[playerIndex] = true;
                }
            }
            else
            {
                isPlayerKeyPressed[playerIndex] = false;

                if (playerEyebrowRaiseDurationTimer[playerIndex] < Time.time)
                {
                    isPlayerEyebrowRaised[playerIndex] = false;
                }
            }
        }

        // This is based on artificer's parry code, with quite a few adjustments!
        private static void EyebrowRaiseExplosion(Player player)
        {
            Vector2 pos2 = player.firstChunk.pos;

            if (Options.vineBoomCosmetics.Value)
            {
                player.room.AddObject(new Explosion.ExplosionLight(pos2, 100.0f, 0.2f, 16, Color.white));
                player.room.AddObject(new ShockWave(pos2, 1000f, 0.05f, 2, false));

                for (int l = 0; l < 10; l++)
                {
                    Vector2 a2 = Custom.RNV();
                    player.room.AddObject(new Spark(pos2 + a2 * Random.value * 40f, a2 * Mathf.Lerp(4f, 30f, Random.value), Color.white, null, 3, 6));
                }
            }

            if (!Options.vineBoomExplosion.Value) return;

            List<Weapon> list = new List<Weapon>();
            for (int m = 0; m < player.room.physicalObjects.Length; m++)
            {
                for (int n = 0; n < player.room.physicalObjects[m].Count; n++)
                {
                    if (player.room.physicalObjects[m][n] is Weapon)
                    {
                        Weapon weapon = player.room.physicalObjects[m][n] as Weapon;
                        if (weapon.mode == Weapon.Mode.Thrown && Custom.Dist(pos2, weapon.firstChunk.pos) < 300f)
                        {
                            list.Add(weapon);
                        }
                    }
                    if (player.room.physicalObjects[m][n] is Creature && player.room.physicalObjects[m][n] != player)
                    {
                        Creature creature = player.room.physicalObjects[m][n] as Creature;

                        if (Custom.Dist(pos2, creature.firstChunk.pos) < 200f && (Custom.Dist(pos2, creature.firstChunk.pos) < 60f || player.room.VisualContact(player.abstractCreature.pos, creature.abstractCreature.pos)))
                        {
                            player.room.socialEventRecognizer.WeaponAttack(null, player, creature, true);
                            creature.SetKillTag(player.abstractCreature);

                            ApplyKnockback(pos2, creature, player);
                        }
                    }
                }
            }
            if (list.Count > 0 && player.room.game.IsArenaSession)
            {
                player.room.game.GetArenaGameSession.arenaSitting.players[0].parries++;
            }
            for (int num6 = 0; num6 < list.Count; num6++)
            {
                list[num6].ChangeMode(Weapon.Mode.Free);
                list[num6].firstChunk.vel = Custom.DegToVec(Custom.AimFromOneVectorToAnother(pos2, list[num6].firstChunk.pos)) * 200.0f;
                list[num6].SetRandomSpin();
            }

        }

        private static void ApplyKnockback(Vector2 pos2, Creature creature, Player player)
        {
            // Do not affect players if friendly fire is off
            if (creature is Player && !Options.eyebrowRaiseFriendlyFire.Value) return;

            // Do not affect held creatures held by the player, unless the option is enabled
            if (!Options.affectsCarried.Value)
            {
                for (int i = 0; i < creature.grabbedBy.Count; i++)
                {
                    if (creature.grabbedBy[i].grabber == player) return;
                }
            }

            // Do not affect carried / carrying players
            if (creature is Player playerCreature)
            {
                // Creature is on our back
                if (!player.isSlugpup && playerCreature == player.slugOnBack.slugcat) return;

                // We are on the creature's back
                if (!playerCreature.isSlugpup && playerCreature.slugOnBack.slugcat == player) return;

                // Do not affect players holding us
                for (int i = 0; i < player.grabbedBy.Count; i++)
                {
                    if (!Options.affectsCarried.Value && player.grabbedBy[i].grabber == playerCreature) return;
                }
            }

            creature.firstChunk.vel = Custom.DegToVec(Custom.AimFromOneVectorToAnother(pos2, creature.firstChunk.pos)) * Options.eyebrowRaisePower.Value;

            // Scavengers have a unique stun mechanic(?)
            if (creature is Scavenger)
            {
                ((Scavenger)creature).HeavyStun(80);
            }
            else if (creature is not Player)
            {
                creature.Stun(80);
            }

            // Force tentacle plants to release you
            if (creature is TentaclePlant)
            {
                for (int num5 = 0; num5 < creature.grasps.Length; num5++)
                {
                    creature.ReleaseGrasp(num5);
                }
            }
        }

        private static void PlayerGraphics_DrawSprites(On.PlayerGraphics.orig_DrawSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            orig(self, sLeaser, rCam, timeStacker, camPos);

            int playerIndex = self.player.playerState.playerNumber;
            int raiseLevel = playerEyebrowRaiseLevel[playerIndex];

            if (Time.time - raiseTimers[playerIndex] > 1.0f / Options.animationFrameRate.Value)
            {
                raiseTimers[playerIndex] = Time.time;

                if (isPlayerEyebrowRaised[playerIndex])
                {
                    if (raiseLevel < Options.animationFrameCount.Value)
                    {
                        raiseLevel++;
                    }
                }
                else if (raiseLevel > 0)
                {
                    raiseLevel--;
                }

                playerEyebrowRaiseLevel[playerIndex] = raiseLevel;
            }

            string? face = GetFace(self, raiseLevel);
            if (face != null) SetFaceSprite(sLeaser, face);
        }

        private static string? GetFace(PlayerGraphics self, int raiseLevel)
        {
            if (raiseLevel == 0) return null;
            
            SlugcatStats.Name name = self.player.SlugCatClass;
            string face = "default";

            if (self.player.dead)
            {
                face = "dead";
            }
            else if (name == MoreSlugcats.MoreSlugcatsEnums.SlugcatStatsName.Artificer)
            {
                face = "artificer";
            }
            else if (name == MoreSlugcats.MoreSlugcatsEnums.SlugcatStatsName.Saint)
            {
                face = "saint";
            }

            if (self.blink > 0 && raiseLevel == Options.animationFrameCount.Value && !self.player.dead)
            {
                face += "_blink";
            }
            else
            {
                face += "_" + raiseLevel;
            }

            return SlugcatEyebrowRaise.MOD_ID + "_" + face;
        }

        private static void SetFaceSprite(RoomCamera.SpriteLeaser sLeaser, string spriteName)
        {
            if (!Futile.atlasManager.DoesContainElementWithName(spriteName))
            {
                SlugcatEyebrowRaise.Logger.LogError($"Missing sprite ({spriteName})! Please check the sprites directory under the mod's folder");
                return;
            }
            sLeaser.sprites[9].element = Futile.atlasManager.GetElementWithName(spriteName);
            sLeaser.sprites[9].scaleX = 1;
        }

        // Henpemaz's magic
        #region Camera Zoom
        private static void RoomCamera_DrawUpdate(On.RoomCamera.orig_DrawUpdate orig, RoomCamera self, float timeStacker, float timeSpeed)
        {
            if (shakeTimer > Time.time)
            {
                self.screenShake = Options.vineBoomLoud.Value ? SHAKE_INTENSITY_LOUD : SHAKE_INTENSITY_NORMAL;
            }

            float zoom = 1f;
            bool zoomed = false;
            Vector2 offset = Vector2.zero;

            #region Follow & Zoom

            // Have camera follow the player
            if (self.room != null && cameraZoomAmount > 0f)
            {
                zoom = cameraZoomAmount * 20f;
                zoomed = true;
                
                Creature? creature = self.followAbstractCreature == null ? null : self.followAbstractCreature.realizedCreature;
                
                if (creature != null)
                {
                    Vector2 value = Vector2.Lerp(creature.bodyChunks[0].lastPos, creature.bodyChunks[0].pos, timeStacker);
                    if (creature.inShortcut)
                    {
                        Vector2? vector = self.room.game.shortcuts.OnScreenPositionOfInShortCutCreature(self.room, creature);
                        if (vector != null)
                        {
                            value = vector.Value;
                        }
                    }
                    offset = new Vector2(self.cameraNumber * 6000f, 0f) + (value - (self.pos + self.sSize / 2f));
                }

            }

            // Zoom in
            if (zoomed)
            {
                // 11 useful layers, the reset is HUD
                for (int i = 0; i < 11; i++)
                {
                    self.SpriteLayers[i].scale = 1.0f;
                    self.SpriteLayers[i].SetPosition(Vector2.zero);
                    self.SpriteLayers[i].ScaleAroundPointRelative(self.sSize / 2f, zoom, zoom);
                }
                self.offset = offset;
            }
            else
            {
                // Unzoom camera on effect slider to 0 or maybe if ChangeRoom didnt call
                for (int i = 0; i < 11; i++)
                {
                    self.SpriteLayers[i].scale = 1f;
                    self.SpriteLayers[i].SetPosition(Vector2.zero);
                }
                self.offset = new Vector2(self.cameraNumber * 6000.0f, 0.0f);
            }

            int randomSeed = 0;

            if (zoomed)
            {
                // deterministic random shake
                randomSeed = Random.seed;
                Random.seed = randomSeed;
            }

            orig(self, timeStacker, timeSpeed);

            #endregion

            if (zoomed)
            {
                Random.seed = randomSeed;
                Vector2 shakeOffset = Vector2.Lerp(self.lastPos, self.pos, timeStacker);   

                if (self.microShake > 0f)
                {
                    shakeOffset += Custom.RNV() * 8f * self.microShake * Random.value;
                }
            
                if (!self.voidSeaMode)
                {
                    shakeOffset.x = Mathf.Clamp(shakeOffset.x, self.CamPos(self.currentCameraPosition).x + self.hDisplace + 8f - 20f, self.CamPos(self.currentCameraPosition).x + self.hDisplace + 8f + 20f);
                    shakeOffset.y = Mathf.Clamp(shakeOffset.y, self.CamPos(self.currentCameraPosition).y + 8f - 7f, self.CamPos(self.currentCameraPosition).y + 33f);
                }
                else
                {
                    shakeOffset.y = Mathf.Min(shakeOffset.y, -528f);
                }

                shakeOffset = new Vector2(Mathf.Floor(shakeOffset.x), Mathf.Floor(shakeOffset.y));
                shakeOffset.x -= 0.02f;
                shakeOffset.y -= 0.02f;

                Vector2 magicOffset = self.CamPos(self.currentCameraPosition) - shakeOffset;
                Vector2 textureOffset = shakeOffset + magicOffset;

                //Vector4 center = new Vector4(
                //	(-shakeOffset.x - 0.5f + self.levelGraphic.width / 2f + self.CamPos(self.currentCameraPosition).x) / self.sSize.x,
                //	(-shakeOffset.y + 0.5f + self.levelGraphic.height / 2f + self.CamPos(self.currentCameraPosition).y) / self.sSize.y,
                //	(-shakeOffset.x - 0.5f + self.levelGraphic.width / 2f + self.CamPos(self.currentCameraPosition).x) / self.sSize.x,
                //	(-shakeOffset.y + 0.5f + self.levelGraphic.height / 2f + self.CamPos(self.currentCameraPosition).y) / self.sSize.y);

                Vector4 center = new Vector4(
                    (magicOffset.x + self.levelGraphic.width / 2f) / self.sSize.x,
                    (magicOffset.y + 2f + self.levelGraphic.height / 2f) / self.sSize.y,
                    (magicOffset.x + self.levelGraphic.width / 2f) / self.sSize.x,
                    (magicOffset.y + 2f + self.levelGraphic.height / 2f) / self.sSize.y);

                shakeOffset += self.offset;
                
                Vector4 spriteRectPos = new Vector4(
                    (-shakeOffset.x + textureOffset.x) / self.sSize.x,
                    (-shakeOffset.y + textureOffset.y) / self.sSize.y,
                    (-shakeOffset.x + self.levelGraphic.width + textureOffset.x) / self.sSize.x,
                    (-shakeOffset.y + self.levelGraphic.height + textureOffset.y) / self.sSize.y);

                //spriteRectPos -= new Vector4(17f / self.sSize.x, 18f / self.sSize.y, 17f / self.sSize.x, 18f / self.sSize.y) * (1f - 1f / zoom);

                spriteRectPos -= center;
                spriteRectPos *= zoom;
                spriteRectPos += center;

                Shader.SetGlobalVector("_spriteRect", spriteRectPos);

                Vector2 zooming = (1f - 1f / zoom) * new Vector2(self.sSize.x / self.room.PixelWidth, self.sSize.y / self.room.PixelHeight);
                Shader.SetGlobalVector("_camInRoomRect", new Vector4(
                    shakeOffset.x / self.room.PixelWidth + zooming.x / 2f,
                    shakeOffset.y / self.room.PixelHeight + zooming.y / 2f,
                    self.sSize.x / self.room.PixelWidth - zooming.x,
                    self.sSize.y / self.room.PixelHeight - zooming.y));

                Shader.SetGlobalVector("_screenSize", self.sSize);
            }
        }
        #endregion

        #region Death IL Hooks
        private static void Player_Die(ILContext il)
        {
            var c = new ILCursor(il);
            while (c.TryGotoNext(MoveType.AfterLabel,
                i => i.MatchLdsfld<SoundID>("Inv_GO")
                ))
            {
                c.Index += 8;
                c.EmitDelegate<Action<Player>>((p) =>
                {
                    p.room.PlaySound(GetVineBoomSoundID(), p.mainBodyChunk);
                });
                break;
            }

            while (c.TryGotoNext(MoveType.AfterLabel,
                x => x.MatchLdsfld<SoundID>("UI_Slugcat_Die")
                ))
            {
                c.Remove();
                c.Emit<Enums.Sounds>(OpCodes.Ldsfld, GetVineBoomStringID());
                break;
            }
        }

        private static void TextPrompt_Update(ILContext il)
        {
            var c = new ILCursor(il);
            while (c.TryGotoNext(MoveType.AfterLabel,
                i => i.MatchLdsfld<SoundID>("HUD_Game_Over_Prompt")
                ))
            {
                c.Remove();
                c.Emit<Enums.Sounds>(OpCodes.Ldsfld, GetVineBoomStringID());
                break;
            }
        }
        #endregion
    }
}
