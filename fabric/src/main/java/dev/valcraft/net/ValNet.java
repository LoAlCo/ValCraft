package dev.valcraft.net;

import dev.valcraft.ValCraft;
import dev.valcraft.combat.ValCombat;
import dev.valcraft.link.ValLink;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.networking.v1.PayloadTypeRegistry;
import net.fabricmc.fabric.api.networking.v1.ServerPlayNetworking;
import net.minecraft.network.RegistryFriendlyByteBuf;
import net.minecraft.network.codec.ByteBufCodecs;
import net.minecraft.network.codec.StreamCodec;
import net.minecraft.network.protocol.common.custom.CustomPacketPayload;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerPlayer;
import org.jspecify.annotations.Nullable;

/**
 * Multiplayer: every player has their own Valheim, talking to their own Minecraft client. The host's
 * Valheim reaches the host's integrated server through shared memory; a guest's Valheim reaches the
 * host's server through these packets instead (its client relays them, see GuestLink).
 */
public final class ValNet {
	private ValNet() {
	}

	private static @Nullable MinecraftServer server;

	/** Guest -> server: the guest's Valheim hit them (as proto::InputEvent kInHurt). */
	public record Hurt(int kind, float valheimDamage, int attackerFormId, int flags) implements CustomPacketPayload {
		public static final Type<Hurt> TYPE = new Type<>(Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, "hurt"));
		public static final StreamCodec<RegistryFriendlyByteBuf, Hurt> CODEC = StreamCodec.composite(
			ByteBufCodecs.VAR_INT, Hurt::kind,
			ByteBufCodecs.FLOAT, Hurt::valheimDamage,
			ByteBufCodecs.INT, Hurt::attackerFormId,
			ByteBufCodecs.VAR_INT, Hurt::flags,
			Hurt::new
		);

		@Override
		public Type<? extends CustomPacketPayload> type() {
			return TYPE;
		}
	}

	/** Server -> guest: the guest died in Minecraft, so their Valheim player dies too. */
	public record Died(int attackerFormId) implements CustomPacketPayload {
		public static final Type<Died> TYPE = new Type<>(Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, "died"));
		public static final StreamCodec<RegistryFriendlyByteBuf, Died> CODEC = StreamCodec.composite(ByteBufCodecs.INT, Died::attackerFormId, Died::new);

		@Override
		public Type<? extends CustomPacketPayload> type() {
			return TYPE;
		}
	}

	/** Guest -> server: Valheim loot for the guest's inventory (count &gt; 0), or building costs out of it (count &lt; 0). */
	public record Give(String item, int count) implements CustomPacketPayload {
		public static final Type<Give> TYPE = new Type<>(Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, "give"));
		public static final StreamCodec<RegistryFriendlyByteBuf, Give> CODEC = StreamCodec.composite(
			ByteBufCodecs.stringUtf8(128), Give::item,
			ByteBufCodecs.VAR_INT, Give::count,
			Give::new
		);

		@Override
		public Type<? extends CustomPacketPayload> type() {
			return TYPE;
		}
	}

	/** Guest -> server: the guest's Valheim moved them (portal, respawn, dungeon door, bed). */
	public record Teleport(double x, double y, double z, float yaw, float pitch) implements CustomPacketPayload {
		public static final Type<Teleport> TYPE = new Type<>(Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, "teleport"));
		public static final StreamCodec<RegistryFriendlyByteBuf, Teleport> CODEC = StreamCodec.composite(
			ByteBufCodecs.DOUBLE, Teleport::x,
			ByteBufCodecs.DOUBLE, Teleport::y,
			ByteBufCodecs.DOUBLE, Teleport::z,
			ByteBufCodecs.FLOAT, Teleport::yaw,
			ByteBufCodecs.FLOAT, Teleport::pitch,
			Teleport::new
		);

		@Override
		public Type<? extends CustomPacketPayload> type() {
			return TYPE;
		}
	}

	/** Guest -> server: the Valheim creatures around the guest (their Valheim's actor table, nearest first). */
	public record Actors(List<ValLink.Actor> actors) implements CustomPacketPayload {
		public static final int MAX = 64;
		public static final Type<Actors> TYPE = new Type<>(Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, "actors"));
		public static final StreamCodec<RegistryFriendlyByteBuf, Actors> CODEC = StreamCodec.of((buf, p) -> {
			int n = Math.min(p.actors.size(), MAX);
			buf.writeVarInt(n);
			for (int i = 0; i < n; i++) {
				ValLink.Actor a = p.actors.get(i);
				buf.writeInt(a.formId());
				buf.writeVarInt(a.flags());
				buf.writeFloat(a.x());
				buf.writeFloat(a.y());
				buf.writeFloat(a.z());
				buf.writeFloat(a.yaw());
				buf.writeFloat(a.width());
				buf.writeFloat(a.height());
				buf.writeFloat(a.healthFrac());
				buf.writeVarInt(a.level());
				buf.writeUtf(a.name(), 32);
			}
		}, buf -> {
			int n = Math.min(buf.readVarInt(), MAX);
			List<ValLink.Actor> list = new ArrayList<>(n);
			for (int i = 0; i < n; i++) {
				list.add(new ValLink.Actor(buf.readInt(), buf.readVarInt(), buf.readFloat(), buf.readFloat(), buf.readFloat(), buf.readFloat(),
					buf.readFloat(), buf.readFloat(), buf.readFloat(), buf.readVarInt(), buf.readUtf(32)));
			}
			return new Actors(list);
		});

		@Override
		public Type<? extends CustomPacketPayload> type() {
			return TYPE;
		}
	}

	/**
	 * Guest -> server: the ground the guest's Valheim describes (its collision messages, deflated), so
	 * mobs, items and the guest themselves stand on it here too. This server otherwise only knows the
	 * host's surroundings. Both describe the same Valheim world, so their pieces fit together.
	 */
	public record Ground(byte[] deflated) implements CustomPacketPayload {
		/** Serverbound custom payloads are capped at 32 KiB. */
		public static final int MAX_BYTES = 30000;
		public static final Type<Ground> TYPE = new Type<>(Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, "ground"));
		public static final StreamCodec<RegistryFriendlyByteBuf, Ground> CODEC = StreamCodec.composite(
			ByteBufCodecs.byteArray(MAX_BYTES + 64), Ground::deflated, Ground::new);

		@Override
		public Type<? extends CustomPacketPayload> type() {
			return TYPE;
		}
	}

	/** Server -> guest: the host's ground was reset (it went into a dungeon, ...): send yours again. */
	public record GroundAgain() implements CustomPacketPayload {
		public static final Type<GroundAgain> TYPE = new Type<>(Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, "ground_again"));
		public static final StreamCodec<RegistryFriendlyByteBuf, GroundAgain> CODEC = StreamCodec.unit(new GroundAgain());

		@Override
		public Type<? extends CustomPacketPayload> type() {
			return TYPE;
		}
	}

	/** Server -> guest: an event for the guest's own Valheim (proto::McEvent: their hit on a creature, a mead, a skill, ...). */
	public record Event(int kind, int formId, float a, float b, float c, float d, int flags, int weapon) implements CustomPacketPayload {
		public static final Type<Event> TYPE = new Type<>(Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, "event"));
		public static final StreamCodec<RegistryFriendlyByteBuf, Event> CODEC = StreamCodec.of((buf, e) -> {
			buf.writeVarInt(e.kind);
			buf.writeInt(e.formId);
			buf.writeFloat(e.a);
			buf.writeFloat(e.b);
			buf.writeFloat(e.c);
			buf.writeFloat(e.d);
			buf.writeInt(e.flags);
			buf.writeInt(e.weapon);
		}, buf -> new Event(buf.readVarInt(), buf.readInt(), buf.readFloat(), buf.readFloat(), buf.readFloat(), buf.readFloat(), buf.readInt(), buf.readInt()));

		@Override
		public Type<? extends CustomPacketPayload> type() {
			return TYPE;
		}
	}

	public static void init() {
		PayloadTypeRegistry.serverboundPlay().register(Hurt.TYPE, Hurt.CODEC);
		PayloadTypeRegistry.serverboundPlay().register(Give.TYPE, Give.CODEC);
		PayloadTypeRegistry.serverboundPlay().register(Teleport.TYPE, Teleport.CODEC);
		PayloadTypeRegistry.serverboundPlay().register(Actors.TYPE, Actors.CODEC);
		PayloadTypeRegistry.serverboundPlay().register(Ground.TYPE, Ground.CODEC);
		PayloadTypeRegistry.clientboundPlay().register(Died.TYPE, Died.CODEC);
		PayloadTypeRegistry.clientboundPlay().register(Event.TYPE, Event.CODEC);
		PayloadTypeRegistry.clientboundPlay().register(GroundAgain.TYPE, GroundAgain.CODEC);
		ServerLifecycleEvents.SERVER_STARTED.register(s -> server = s);
		ServerLifecycleEvents.SERVER_STOPPED.register(s -> server = null);
		ServerPlayNetworking.registerGlobalReceiver(Hurt.TYPE, (payload, context) -> {
			ServerPlayer player = context.player();
			// A hit's worth of damage, whatever the guest's client claims (friends only, but still).
			float damage = Math.max(0.0F, Math.min(payload.valheimDamage(), 10000.0F));
			context.server().execute(() -> ValCombat.hurtPlayer(player, payload.kind(), damage, payload.attackerFormId(), payload.flags()));
		});
		ServerPlayNetworking.registerGlobalReceiver(Give.TYPE, (payload, context) -> {
			ServerPlayer player = context.player();
			int count = Math.max(-4096, Math.min(payload.count(), 4096));
			context.server().execute(() -> giveOrTake(player, payload.item(), count));
		});
		ServerPlayNetworking.registerGlobalReceiver(Teleport.TYPE, (payload, context) -> {
			ServerPlayer player = context.player();
			context.server().execute(() -> {
				player.teleportTo(payload.x(), payload.y(), payload.z());
				player.setYRot(payload.yaw());
				player.setXRot(payload.pitch());
				player.resetFallDistance();
			});
		});
		ServerPlayNetworking.registerGlobalReceiver(Actors.TYPE, (payload, context) -> {
			UUID id = context.player().getUUID();
			context.server().execute(() -> ValCombat.guestActors(id, payload.actors()));
		});
		ServerPlayNetworking.registerGlobalReceiver(Ground.TYPE, (payload, context) -> dev.valcraft.world.ValCollision.receiveForwarded(payload.deflated()));
		// The host's Valheim wiped its ground (world change, dungeon): the guests' goes with it, so they send theirs again.
		dev.valcraft.world.ValCollision.onCleared(() -> {
			MinecraftServer s = server;
			if (s != null) {
				s.execute(() -> {
					for (ServerPlayer p : s.getPlayerList().getPlayers()) {
						if (!isHost(p) && ServerPlayNetworking.canSend(p, GroundAgain.TYPE)) {
							ServerPlayNetworking.send(p, new GroundAgain());
						}
					}
				});
			}
		});
	}

	/** True if this player plays on this machine (their Valheim is on the shared-memory link). */
	public static boolean isHost(ServerPlayer player) {
		var server = player.level().getServer();
		return server != null && server.isSingleplayerOwner(player.nameAndId());
	}

	/**
	 * An event for this player's own Valheim: the host's through the link, a guest's through their
	 * client (which puts it on their link). {@code player} null: the host's.
	 */
	public static void pushEvent(@Nullable ServerPlayer player, int kind, int formId, float a, float b, float c, float d, int flags, int weapon) {
		if (player == null || isHost(player)) {
			if (ValLink.active()) {
				ValLink.pushEvent(kind, formId, a, b, c, d, flags, weapon);
			}
			return;
		}
		if (ServerPlayNetworking.canSend(player, Event.TYPE)) {
			ServerPlayNetworking.send(player, new Event(kind, formId, a, b, c, d, flags, weapon));
		}
	}

	public static void pushEvent(@Nullable ServerPlayer player, int kind, int formId, float a, float b, float c, float d, int flags) {
		pushEvent(player, kind, formId, a, b, c, d, flags, 0);
	}

	/** An event for every player's Valheim (what each of them should see: an arrow stuck in a creature). */
	public static void pushEventAll(MinecraftServer server, int kind, int formId, float a, float b, float c, float d, int flags, int weapon) {
		boolean host = false;
		for (ServerPlayer p : server.getPlayerList().getPlayers()) {
			host |= isHost(p);
			pushEvent(p, kind, formId, a, b, c, d, flags, weapon);
		}
		if (!host) {
			pushEvent(null, kind, formId, a, b, c, d, flags, weapon);
		}
	}

	/** An event for the Valheim of the player nearest this spot (an explosion: their Valheim has the ground and creatures there). */
	public static void pushEventNearest(net.minecraft.server.level.ServerLevel level, double x, double y, double z, int kind, int formId, float a, float b,
		float c, float d, int flags) {
		ServerPlayer nearest = null;
		double best = Double.MAX_VALUE;
		for (ServerPlayer p : level.players()) {
			double dist = p.distanceToSqr(x, y, z);
			if (dist < best) {
				best = dist;
				nearest = p;
			}
		}
		pushEvent(nearest, kind, formId, a, b, c, d, flags, 0);
	}

	/** Valheim loot picked up by the player: the matching Minecraft item into the inventory (dropped at the feet if full); negative: building costs out. */
	public static void giveOrTake(ServerPlayer player, String id, int count) {
		if (count == 0 || player.isRemoved()) {
			return;
		}
		var key = Identifier.tryParse(id);
		var item = key == null ? java.util.Optional.<net.minecraft.world.item.Item>empty() : net.minecraft.core.registries.BuiltInRegistries.ITEM.getOptional(key);
		if (item.isEmpty() || item.get() == net.minecraft.world.item.Items.AIR) {
			ValCraft.LOG.warn("ValCraft: Valheim loot maps to unknown item {}", id);
			return;
		}
		if (count < 0) {
			take(player, item.get(), -count);
			return;
		}
		int left = count;
		while (left > 0) {
			int n = Math.min(left, item.get().getDefaultMaxStackSize());
			var stack = new net.minecraft.world.item.ItemStack(item.get(), n);
			if (!player.getInventory().add(stack) && !stack.isEmpty()) {
				player.spawnAtLocation(player.level(), stack);
			}
			left -= n;
		}
		player.containerMenu.broadcastChanges();
		if (!isHost(player)) {
			ValCraft.LOG.info("ValCraft: guest {} got {} x{} from their Valheim", player.getPlainTextName(), id, count);
		}
		player.level().playSound(null, player.getX(), player.getY(), player.getZ(), net.minecraft.sounds.SoundEvents.ITEM_PICKUP,
			net.minecraft.sounds.SoundSource.PLAYERS, 0.2F, 1.4F + player.getRandom().nextFloat() * 0.4F);
	}

	/** Valheim building spent materials: they come out of the Minecraft inventory (nothing in creative). */
	private static void take(ServerPlayer player, net.minecraft.world.item.Item item, int count) {
		if (player.getAbilities().instabuild) {
			return;
		}
		int left = count;
		for (var stack : player.getInventory().getNonEquipmentItems()) {
			if (left <= 0) {
				break;
			}
			if (stack.is(item)) {
				int n = Math.min(left, stack.getCount());
				stack.shrink(n);
				left -= n;
			}
		}
		player.getInventory().setChanged();
		player.containerMenu.broadcastChanges();
	}
}
