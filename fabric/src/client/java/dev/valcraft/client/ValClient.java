package dev.valcraft.client;

import dev.valcraft.ValCraft;
import dev.valcraft.client.render.WorldExporter;
import dev.valcraft.link.Proto;
import dev.valcraft.link.ValLink;
import dev.valcraft.world.ValCollision;
import net.minecraft.client.Camera;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.phys.Vec3;
import org.lwjgl.sdl.SDLVideo;

/**
 * Per-frame glue between the Minecraft client and Valheim. Everything here runs on the render
 * thread, called from MinecraftMixin.
 */
public final class ValClient {
	private static final boolean SHOW_WINDOW = Boolean.getBoolean("valcraft.showWindow");
	// Started by Valheim (ValCraft's bundled instance passes -Dvalcraft.startHidden=true): no window and
	// no title-screen music from the first frame, even while Valheim is paused (Alt-Tabbed) and the
	// two haven't linked up yet. Otherwise the window only goes once Valheim is there.
	private static final boolean START_HIDDEN = Boolean.getBoolean("valcraft.startHidden");
	private static boolean startedHidden;

	private static final ValLink.ValState sky = new ValLink.ValState();
	/** The last ValState.fovSeq whose [Camera] FOV was put into Minecraft's FOV option. */
	private static int fovApplied;

	/**
	 * Valheim's [Camera] FOV is Minecraft's FOV option: when it changes in Valheim's config it's set
	 * here (and saved); when the player changes it in Minecraft's options, Valheim's config follows
	 * (it sees optionsFov, once fovAck says its own change has arrived).
	 */
	private static void applyFov(Minecraft minecraft) {
		if (sky.fovSeq == fovApplied || sky.fovSetting < 1.0F) {
			return;
		}
		fovApplied = sky.fovSeq;
		int fov = Math.max(30, Math.min(110, Math.round(sky.fovSetting)));
		if (minecraft.options.fov().get() != fov) {
			minecraft.options.fov().set(fov);
			minecraft.options.save();
			ValCraft.LOG.info("ValCraft: FOV {} from Valheim's config", fov);
		}
	}
	private static final ValLink.McState mc = new ValLink.McState();
	private static volatile boolean linked;
	private static boolean tookOver;
	private static boolean windowHidden;
	private static int appliedViewportW, appliedViewportH;

	// Teleport / hold state: Valheim decides where the player is after loads, doors and respawns.
	private static int lastTeleportSeq = -1;
	private static int teleportAck;
	private static boolean teleportPending;
	// A new player (joining, respawning, or F8 switching saves): once put down, it must not die
	// from a fall, e.g. when the blocks it stood on only exist in the other save.
	private static boolean landSafely, safeFalling;
	private static boolean addedSlowFalling;  // server thread only
	private static long safeFallUntil;
	private static LocalPlayer lastPlayer;
	private static Vec3 holdPos;
	private static Vec3 unlinkedHold;
	private static long holdSince;
	private static long qpcFreq;
	private static LocalPlayer eyePlayer;
	private static float eyeSmoothed;
	private static long frameCounter;
	private static int lastPacedSeq;
	private static boolean valheimStalled;
	private static int exporterErrors;

	private ValClient() {
	}

	public static boolean linked() {
		return linked;
	}

	/**
	 * True once Valheim has connected in this session. From then on Minecraft never touches the
	 * real mouse or keyboard again (even if Valheim closes), since its window is hidden.
	 */
	public static boolean tookOver() {
		return tookOver;
	}

	/** Valheim's game is paused (only ever in single player). */
	public static boolean valheimPaused() {
		return linked && sky.paused();
	}

	public static ValLink.ValState sky() {
		return sky;
	}

	/** Start of Minecraft.runTick: pull state and input from Valheim before anything else runs. */
	public static void beginFrame() {
		ValLink.poll();
		quitWithValheim(Minecraft.getInstance());
		if (START_HIDDEN && !startedHidden) {
			startedHidden = true;
			Minecraft minecraft = Minecraft.getInstance();
			hideWindowOnce(minecraft);
			minecraft.options.getSoundSourceOptionInstance(net.minecraft.sounds.SoundSource.MUSIC).set(0.0);
			minecraft.getMusicManager().stopPlaying();
		}
		boolean nowLinked = ValLink.active();
		if (nowLinked) {
			ValLink.readValState(sky); // on a torn read we simply keep last frame's state
			applyFov(Minecraft.getInstance());
			dev.valcraft.world.ValWater.refresh();
		} else {
			dev.valcraft.world.ValWater.clear();
		}
		if (nowLinked != linked) {
			linked = nowLinked;
			ValCraft.LOG.info("ValCraft: Valheim link {}", linked ? "up" : "down");
			if (linked) {
				tookOver = true;
				unlinkedHold = null;
				ValCollision.startConsumer();
				applyLinkedOptions();
			} else {
				InputBridge.releaseAll();
				LocalPlayer player = Minecraft.getInstance().player;
				unlinkedHold = player != null ? player.position() : null;
			}
		}
		if (!linked) {
			return;
		}

		Minecraft minecraft = Minecraft.getInstance();
		hideWindowOnce(minecraft);
		applyViewportSize(minecraft);
		MirrorWorld.openWhenReady(minecraft);
		MirrorWorld.followValheimWorld(minecraft);

		if (sky.menuOpen() || sky.loading()) {
			InputBridge.releaseAll();
		}
		InputBridge.drain(minecraft);
		ProxySync.frame(minecraft);

		LocalPlayer player = minecraft.player;
		if (player == null) {
			lastPlayer = null;
			return;
		}

		// A new player object means we just joined or respawned: put it where Valheim's player is.
		if (player != lastPlayer) {
			lastPlayer = player;
			teleportPending = true;
			landSafely = true;
		}
		if (sky.teleportSeq != lastTeleportSeq) {
			lastTeleportSeq = sky.teleportSeq;
			teleportPending = true;
		}
		if (teleportPending && sky.inGame() && !sky.loading()) {
			requestTeleport(minecraft, sky.x, sky.y, sky.z, sky.yaw, sky.pitch);
			teleportAck = sky.teleportSeq;
			teleportPending = false;
			holdPos = new Vec3(sky.x, sky.y, sky.z);
		}

		// Look direction is driven by Valheim (zero-latency camera); MC uses it for everything else.
		if (minecraft.gui.screen() == null) {
			// Valheim sends the yaw wrapped to 0..360; Minecraft's own yaw is continuous and its
			// blends (hand sway, body turn) spin the long way round across a wrap. Keep it continuous.
			float yaw = player.getYRot() + net.minecraft.util.Mth.wrapDegrees(sky.yaw - player.getYRot());
			player.setYRot(yaw);
			player.setXRot(sky.pitch);
			player.yRotO = yaw;
			player.xRotO = sky.pitch;
		}
	}

	// The hand's sway (it trails the view a little when turning), computed every frame: Minecraft eases
	// it toward the look once per tick, which, with the look set from Valheim every frame and its yaw
	// wrapped to 0..360, lagged far behind on fast turns and snapped back. Same feel: Minecraft halves
	// the gap each 50 ms tick, a time constant of about 72 ms.
	private static final double SWAY_SECONDS = 0.072;
	private static float swayYaw, swayPitch;
	private static long swayNanos;
	private static LocalPlayer swayPlayer;

	/** Just before GameRenderer.extract() (where the hand's sway is captured), after this frame's ticks. */
	public static void beforeRender() {
		LocalPlayer player = Minecraft.getInstance().player;
		if (!linked || player == null) {
			swayPlayer = null;
			return;
		}
		float yaw = player.getYRot(), pitch = player.getXRot();
		long now = System.nanoTime();
		if (player != swayPlayer) {
			swayPlayer = player;
			swayYaw = yaw;
			swayPitch = pitch;
			swayNanos = now;
		}
		double dt = Math.min((now - swayNanos) / 1.0e9, 0.1);
		swayNanos = now;
		float k = (float) (1.0 - Math.exp(-dt / SWAY_SECONDS));
		// The short way round, so crossing 0/360 is a small step, not a full turn.
		float behind = net.minecraft.util.Mth.wrapDegrees(yaw - swayYaw);
		swayYaw = yaw - behind * (1.0F - k);
		swayPitch += (pitch - swayPitch) * k;
		// Both the tick's old and new value: Minecraft's partial-tick blend then adds nothing.
		player.yBob = player.yBobO = swayYaw;
		player.xBob = player.xBobO = swayPitch;
	}

	// Minecraft is started with Valheim (the SKSE plugin launches it), so it goes when that Valheim has
	// closed for good: saved and shut down the normal way. -Dvalcraft.quitWithValheim=false keeps it
	// running instead (development: restarting Valheim without restarting Minecraft).
	private static final boolean QUIT_WITH_VALHEIM = Boolean.parseBoolean(System.getProperty("valcraft.quitWithValheim", "true"));
	private static long valheimGoneSince;
	private static long nextValheimCheck;

	private static void quitWithValheim(Minecraft minecraft) {
		int pid = ValLink.valheimPid();
		long now = System.currentTimeMillis();
		if (!QUIT_WITH_VALHEIM || pid == 0 || now < nextValheimCheck) {
			return;
		}
		nextValheimCheck = now + 1000;
		if (ProcessHandle.of(pid).map(ProcessHandle::isAlive).orElse(false)) {
			valheimGoneSince = 0;
			return;
		}
		if (valheimGoneSince == 0) {
			valheimGoneSince = now;
		} else if (now - valheimGoneSince > 5000) {
			ValCraft.LOG.info("ValCraft: Valheim (pid {}) has closed; saving and quitting", pid);
			minecraft.stop();
		}
	}

	/** Called at the end of every client tick. */
	public static void clientTick(Minecraft minecraft) {
		MirrorWorld.tick(minecraft);
		if (linked) {
			SharedWorld.tick(minecraft);
			GuestLink.tick(minecraft);
			BossBars.tick(minecraft);
			dev.valcraft.client.render.MobExporter.tick(minecraft);
		}
		DiscordPresence.tick(minecraft);
		freezeWhileUnlinked(minecraft);
		holdUntilReady(minecraft);
		tickSafeFall(minecraft);
		ValHarvest.tick(minecraft);
		Vec3 held = ValCollider.takeHold();
		if (held != null && minecraft.player != null) {
			// Collision ahead hadn't arrived (see ValCollider): stay put this tick, keep the momentum.
			minecraft.player.setPos(held.x, held.y, held.z);
			minecraft.player.xo = held.x;
			minecraft.player.yo = held.y;
			minecraft.player.zo = held.z;
			minecraft.player.resetFallDistance();
		}
		publishTick(minecraft);
	}

	/**
	 * Valheim went quiet (a long loading screen, a stall, or it closed). Its collision around the
	 * player may be about to change (interior doors), so keep the player exactly where they were
	 * instead of letting them fall; Valheim puts them where they belong when it's back.
	 */
	private static void freezeWhileUnlinked(Minecraft minecraft) {
		LocalPlayer player = minecraft.player;
		if (linked || !tookOver || player == null) {
			return;
		}
		if (unlinkedHold == null) {
			unlinkedHold = player.position();
		}
		player.setDeltaMovement(Vec3.ZERO);
		player.setPos(unlinkedHold.x, unlinkedHold.y, unlinkedHold.z);
		player.xo = unlinkedHold.x;
		player.yo = unlinkedHold.y;
		player.zo = unlinkedHold.z;
		player.resetFallDistance();
	}

	/**
	 * Hands Valheim the raw physics tick (previous + latest feet, smoothed eye height, walk bob) with a
	 * QueryPerformanceCounter timestamp. Valheim interpolates between them on its own frame clock,
	 * exactly like Minecraft's renderer does with partial ticks.
	 */
	private static void publishTick(Minecraft minecraft) {
		LocalPlayer player = minecraft.player;
		if (!linked || player == null) {
			return;
		}
		if (qpcFreq == 0) {
			qpcFreq = ValLink.qpcFrequency();
		}
		float tickMs = minecraft.level != null ? minecraft.level.tickRateManager().millisecondsPerTick() : 50.0F;
		// The tick really "happened" partial ticks ago (DeltaTracker keeps the remainder).
		float remainder = minecraft.getDeltaTracker().getGameTimeDeltaPartialTick(false);
		mc.tickQpc = ValLink.qpc() - (long) (remainder * tickMs * qpcFreq / 1000.0);
		mc.tickMs = tickMs;
		mc.prevX = player.xo;
		mc.prevY = player.yo;
		mc.prevZ = player.zo;
		mc.curX = player.getX();
		mc.curY = player.getY();
		mc.curZ = player.getZ();
		// Same smoothing as Camera.tick(): eye height eases halfway toward the target each tick.
		if (player != eyePlayer) {
			eyePlayer = player;
			eyeSmoothed = player.getEyeHeight();
		}
		mc.eyeHeightO = eyeSmoothed;
		eyeSmoothed += (player.getEyeHeight() - eyeSmoothed) * 0.5F;
		mc.eyeHeightT = eyeSmoothed;
		boolean bob = minecraft.options.bobView().get();
		var avatar = player.avatarState();
		mc.walkDistO = bob ? avatar.getInterpolatedWalkDistance(0.0F) : 0.0F;
		mc.walkDist = bob ? avatar.getInterpolatedWalkDistance(1.0F) : 0.0F;
		mc.bobO = bob ? avatar.getInterpolatedBob(0.0F) : 0.0F;
		mc.bob = bob ? avatar.getInterpolatedBob(1.0F) : 0.0F;
		ValLink.writeMcState(mc);
	}

	/**
	 * A light in hand (a torch, lantern, glowstone, ... any block item that glows brightly):
	 * Valheim lights the player the way its own torch does (HeldLight.cs). Soul ones glow blue.
	 */
	private static int heldLight(net.minecraft.world.item.ItemStack stack) {
		if (stack.isEmpty() || !(stack.getItem() instanceof net.minecraft.world.item.BlockItem item)) {
			return 0;
		}
		if (item.getBlock().defaultBlockState().getLightEmission() < 10) {
			return 0;
		}
		String id = net.minecraft.core.registries.BuiltInRegistries.ITEM.getKey(stack.getItem()).getPath();
		return Proto.MC_HOLDING_LIGHT | (id.contains("soul") ? Proto.MC_HOLDING_SOUL_LIGHT : 0);
	}

	/** Freeze the player until Valheim's collision around them has arrived. */
	private static void holdUntilReady(Minecraft minecraft) {
		LocalPlayer player = minecraft.player;
		if (!linked || player == null) {
			return;
		}
		if (!sky.inGame() || sky.loading()) {
			// Valheim is on its main menu or a loading screen: park the player where they are.
			if (holdPos == null) {
				holdPos = player.position();
			}
			teleportPending = true;
		}
		if (holdPos == null) {
			holdSince = 0;
			return;
		}
		if (holdSince == 0) {
			holdSince = System.currentTimeMillis();
		}
		int bx = (int) Math.floor(holdPos.x), by = (int) Math.floor(holdPos.y), bz = (int) Math.floor(holdPos.z);
		boolean known = ValCollision.isKnown(bx, by - 1, bz) && ValCollision.isKnown(bx, by, bz)
			&& ValCollision.isKnown(bx, by - ValCollision.REGION_SIZE, bz);
		// Release once there is actual ground below (or after a timeout, e.g. when mid-air on purpose).
		boolean ready = known && (ValCollision.hasSolidBelow(bx, by, bz, 12) || System.currentTimeMillis() - holdSince > 6000);
		// Block terrain: wait for the blocks under the player to be built too (at most 8 s).
		if ((sky.flags & Proto.VAL_BLOCK_TERRAIN) != 0) {
			ready = known && dev.valcraft.world.TerrainGen.isBuilt(Math.floorDiv(bx, 16), Math.floorDiv(bz, 16))
				|| System.currentTimeMillis() - holdSince > 8000;
		}
		if (ready && sky.inGame() && !sky.loading()) {
			// Valheim's feet can sit a fraction of a voxel inside our ground layer. Minecraft's
			// collision never pushes you out of a shape, so you'd drop through: lift out first.
			Vec3 safe = liftOutOfGeometry(player, holdPos);
			if (safe.y != holdPos.y) {
				player.setPos(safe.x, safe.y, safe.z);
				player.yo = safe.y;
				ValCraft.LOG.info("ValCraft: put player {} blocks {} onto the ground", String.format("%.3f", Math.abs(safe.y - holdPos.y)), safe.y > holdPos.y ? "up" : "down");
			}
			holdPos = null;
			if (landSafely) {
				landSafely = false;
				startSafeFall(minecraft);
			}
			return;
		}
		player.setDeltaMovement(Vec3.ZERO);
		player.setPos(holdPos.x, holdPos.y, holdPos.z);
		player.xo = holdPos.x;
		player.yo = holdPos.y;
		player.zo = holdPos.z;
		player.resetFallDistance();
	}

	/**
	 * Hidden Slow Falling until the player lands (at most 15 s): no fall damage, and a gentle drift
	 * down if the floor it stood on isn't in this save. Fall damage is the integrated server's, so
	 * the effect goes on the server player; a Slow Falling of the player's own is left alone.
	 */
	private static void startSafeFall(Minecraft minecraft) {
		LocalPlayer player = minecraft.player;
		if (player == null || player.onGround()) {
			return;
		}
		safeFalling = true;
		safeFallUntil = System.currentTimeMillis() + 15000;
		var server = minecraft.getSingleplayerServer();
		if (server == null) {
			return;  // a friend's world: resetting the fall distance below is all we can do
		}
		var uuid = player.getUUID();
		server.execute(() -> {
			ServerPlayer sp = server.getPlayerList().getPlayer(uuid);
			if (sp != null && !sp.hasEffect(net.minecraft.world.effect.MobEffects.SLOW_FALLING)) {
				sp.addEffect(new net.minecraft.world.effect.MobEffectInstance(net.minecraft.world.effect.MobEffects.SLOW_FALLING, 15 * 20, 0, false, false, false));
				sp.resetFallDistance();
				addedSlowFalling = true;
			}
		});
		ValCraft.LOG.info("ValCraft: put down in mid-air after a world change: falling safely");
	}

	private static void tickSafeFall(Minecraft minecraft) {
		LocalPlayer player = minecraft.player;
		if (!safeFalling || player == null) {
			return;
		}
		player.resetFallDistance();
		if (!player.onGround() && !player.isInWater() && System.currentTimeMillis() < safeFallUntil) {
			return;
		}
		safeFalling = false;
		var server = minecraft.getSingleplayerServer();
		if (server == null) {
			return;
		}
		// On the server thread, after the task that added it (they run in order).
		var uuid = player.getUUID();
		server.execute(() -> {
			if (!addedSlowFalling) {
				return;
			}
			addedSlowFalling = false;
			ServerPlayer sp = server.getPlayerList().getPlayer(uuid);
			if (sp != null) {
				sp.resetFallDistance();
				sp.removeEffect(net.minecraft.world.effect.MobEffects.SLOW_FALLING);
			}
		});
	}

	private static Vec3 liftOutOfGeometry(LocalPlayer player, Vec3 pos) {
		// Stand on the exact Valheim ground if the feet are a little inside it (up to a step). Not
		// further: a door frame or roof just overhead (dungeon exits) would lift the player onto it.
		double ground = ValCollider.groundAt(pos.x, pos.y, pos.z, 0.6);
		if (Double.isNaN(ground)) {
			return pos;
		}
		if (ground > pos.y) {
			return new Vec3(pos.x, ground, pos.z);
		}
		// Valheim puts players down a little above where they stand (dungeon exits, portals): settle
		// onto the ground below instead of dropping onto it.
		return pos.y - ground < 2.5 ? new Vec3(pos.x, ground, pos.z) : pos;
	}

	private static void requestTeleport(Minecraft minecraft, double x, double y, double z, float yaw, float pitch) {
		LocalPlayer player = minecraft.player;
		player.setPos(x, y, z);
		player.setDeltaMovement(Vec3.ZERO);
		player.resetFallDistance();
		var server = minecraft.getSingleplayerServer();
		if (server != null) {
			var uuid = player.getUUID();
			server.execute(() -> {
				ServerPlayer sp = server.getPlayerList().getPlayer(uuid);
				if (sp != null) {
					sp.teleportTo(x, y, z);
					sp.setYRot(yaw);
					sp.setXRot(pitch);
					sp.resetFallDistance();
				}
			});
		} else if (net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking.canSend(dev.valcraft.net.ValNet.Teleport.TYPE)) {
			// A guest in a friend's world: its server moves us too (it would otherwise call this a cheat and pull us back).
			net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking.send(new dev.valcraft.net.ValNet.Teleport(x, y, z, yaw, pitch));
		}
		ValCraft.LOG.info("ValCraft: teleported to {} {} {}", x, y, z);
	}

	/** After GameRenderer.render(): report the player to Valheim and ship the overlay frame. */
	public static void afterRender() {
		if (!linked) {
			return;
		}
		Minecraft minecraft = Minecraft.getInstance();
		LocalPlayer player = minecraft.player;
		int flags = 0;
		if (player != null && minecraft.level != null) {
			float partial = minecraft.getDeltaTracker().getGameTimeDeltaPartialTick(false);
			Vec3 feet = player.getPosition(partial);
			Camera camera = minecraft.gameRenderer.mainCamera();
			flags |= Proto.MC_IN_WORLD;
			if (player.onGround()) {
				flags |= Proto.MC_ON_GROUND;
			}
			if (player.isShiftKeyDown()) {
				flags |= Proto.MC_SNEAKING;
			}
			if (player.isSprinting()) {
				flags |= Proto.MC_SPRINTING;
			}
			if (player.isDeadOrDying()) {
				flags |= Proto.MC_DEAD;
			}
			if (player.isSwimming()) {
				flags |= Proto.MC_SWIMMING;
			}
			if (player.getAbilities().flying) {
				flags |= Proto.MC_FLYING;
			}
			if (player.isEyeInFluid(net.minecraft.tags.FluidTags.WATER)) {
				flags |= Proto.MC_EYE_IN_WATER;
			}
			if (player.isEyeInFluid(net.minecraft.tags.FluidTags.LAVA)) {
				flags |= Proto.MC_EYE_IN_LAVA;
			}
			if (player.getMainHandItem().is(net.minecraft.tags.ItemTags.HOES)) {
				flags |= Proto.MC_HOLDING_HOE;
			}
			if (player.getAbilities().instabuild) {
				flags |= Proto.MC_CREATIVE;
			}
			if (player.getMainHandItem().is(dev.valcraft.item.ValItems.BUILD_HAMMER)) {
				flags |= Proto.MC_HOLDING_HAMMER;
			}
			flags |= heldLight(player.getMainHandItem()) | heldLight(player.getOffhandItem());
			if (player.getVehicle() instanceof net.minecraft.world.entity.vehicle.boat.AbstractBoat) {
				flags |= Proto.MC_IN_BOAT;
			}
			mc.x = feet.x;
			mc.y = feet.y;
			mc.z = feet.z;
			mc.yaw = player.getYRot();
			mc.pitch = player.getXRot();
			// The eye, not the camera: in third person Minecraft's camera sits behind or in front.
			Vec3 eye = camera.isDetached() ? player.getEyePosition(partial) : camera.position();
			mc.eyeHeight = (float) (eye.y - feet.y);
			mc.eyeX = eye.x;
			mc.eyeY = eye.y;
			mc.eyeZ = eye.z;
			mc.fov = camera.getFov();
			// Minecraft's F5 camera: Valheim puts its camera where Minecraft's would be.
			mc.cameraMode = minecraft.options.getCameraType().ordinal();
			mc.cameraDistance = camera.isDetached() ? (float) camera.position().distanceTo(player.getEyePosition(partial)) : 0.0F;
			mc.optionsFov = minecraft.options.fov().get();
			mc.fovAck = fovApplied;
			// Walk bob, exactly what GameRenderer.bobView() uses this frame.
			var entityState = minecraft.gameRenderer.gameRenderState().levelRenderState.cameraRenderState.entityRenderState;
			boolean bob = minecraft.options.bobView().get() && entityState.isPlayer;
			mc.bobPhase = bob ? entityState.backwardsInterpolatedWalkDistance : 0.0F;
			mc.bobAmount = bob ? entityState.bob : 0.0F;
		}
		if (minecraft.gui.screen() != null) {
			flags |= Proto.MC_SCREEN_OPEN;
		}
		mc.flags = flags;
		mc.sensitivity = minecraft.options.sensitivity().get().floatValue();
		mc.teleportAck = holdPos == null ? teleportAck : teleportAck - 1; // not "arrived" until we are released
		mc.guiScale = minecraft.getWindow().getGuiScale();
		mc.frameCounter = ++frameCounter;
		SharedWorld.report(minecraft, mc);
		ValLink.writeMcState(mc);

		if ((flags & Proto.MC_IN_WORLD) != 0) {
			try {
				WorldExporter.frame(minecraft, minecraft.getDeltaTracker().getGameTimeDeltaPartialTick(false));
			} catch (RuntimeException e) {
				if (exporterErrors++ < 5) {
					ValCraft.LOG.error("ValCraft: world export failed", e);
				}
			}
			FrameExporter.capture(minecraft);
		}
	}

	/** End of the frame: render at most once per Valheim frame instead of spinning freely. */
	public static void paceFrame() {
		if (!linked) {
			return;
		}
		if (valheimStalled && (ValLink.valStateSeq() >>> 1) == lastPacedSeq) {
			return; // Valheim is paused (menu / alt-tab): don't block every frame waiting for it
		}
		valheimStalled = false;
		long deadline = System.nanoTime() + 25_000_000L;
		// ValState.seq advances by 2 per Valheim frame (odd while writing).
		while ((ValLink.valStateSeq() >>> 1) == lastPacedSeq && System.nanoTime() < deadline) {
			Thread.onSpinWait();
			if (deadline - System.nanoTime() > 2_000_000L) {
				Thread.yield();
			}
		}
		int seqNow = ValLink.valStateSeq() >>> 1;
		valheimStalled = seqNow == lastPacedSeq;
		lastPacedSeq = seqNow;
	}

	private static void applyLinkedOptions() {
		Minecraft minecraft = Minecraft.getInstance();
		var options = minecraft.options;
		options.pauseOnLostFocus = false;
		options.vignette().set(false);
		options.enableVsync().set(false);
		options.framerateLimit().set(260);
		// Minecraft doesn't draw the world itself; these only decide how far out placed blocks,
		// arrows and Valheim NPC stand-ins stay loaded and simulated.
		options.renderDistance().set(8);
		options.simulationDistance().set(8);
		options.autoJump().set(false);
		options.onboardAccessibility = false;
		if (options.tutorialStep != net.minecraft.client.tutorial.TutorialSteps.NONE) {
			minecraft.getTutorial().setStep(net.minecraft.client.tutorial.TutorialSteps.NONE);
		}
		options.getSoundSourceOptionInstance(net.minecraft.sounds.SoundSource.MUSIC).set(0.0);
		// Minecraft's rain and thunder follow Valheim's (WeatherSync), which plays its own
		options.getSoundSourceOptionInstance(net.minecraft.sounds.SoundSource.WEATHER).set(0.0);
		options.save();
	}

	private static void hideWindowOnce(Minecraft minecraft) {
		if (windowHidden || SHOW_WINDOW) {
			return;
		}
		windowHidden = true;
		SDLVideo.SDL_HideWindow(minecraft.getWindow().handle());
		ValCraft.LOG.info("ValCraft: game window hidden (run with -Dvalcraft.showWindow=true to keep it)");
	}

	private static void applyViewportSize(Minecraft minecraft) {
		int w = Math.min(sky.viewportW, Proto.MAX_OVERLAY_W);
		int h = Math.min(sky.viewportH, Proto.MAX_OVERLAY_H);
		if (w <= 0 || h <= 0 || (w == appliedViewportW && h == appliedViewportH)) {
			return;
		}
		appliedViewportW = w;
		appliedViewportH = h;
		minecraft.getWindow().setWindowed(w, h);
		ValCraft.LOG.info("ValCraft: sizing overlay to Valheim viewport {}x{}", w, h);
	}
}
