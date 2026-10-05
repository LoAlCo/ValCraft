package dev.valcraft.client;

import dev.valcraft.ValCraft;
import dev.valcraft.link.Proto;
import dev.valcraft.link.ValLink;
import dev.valcraft.net.ValNet;
import dev.valcraft.world.ValCollision;
import java.io.ByteArrayOutputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentLinkedQueue;
import java.util.zip.Deflater;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking;
import net.minecraft.client.Minecraft;

/**
 * Multiplayer, a guest in a friend's world: the host's server knows only the host's Valheim, so this
 * hands it what ours says. Our Valheim's ground (its collision messages, kept so they can go again
 * when we join or the host's is reset), the creatures around us, our loot and building costs, and
 * where our Valheim moves us. Events the server has for our Valheim (our hits on creatures, meads)
 * come back as ValNet.Event and go on our own link.
 */
public final class GuestLink {
	/** Ground pieces kept to send again (newest last); each is a few KB. */
	private static final int CACHE_MAX = 2500;
	private static final int BATCH_RAW_BYTES = 96 * 1024;
	private static final int PACKETS_PER_TICK = 4;

	private record Key(int type, int a, int b, int c) {
	}

	private static final Map<Key, List<byte[]>> CACHE = new LinkedHashMap<>(256, 0.75F, true) {
		@Override
		protected boolean removeEldestEntry(Map.Entry<Key, List<byte[]>> eldest) {
			return size() > CACHE_MAX;
		}
	};
	private static final ConcurrentLinkedQueue<byte[]> OUTBOX = new ConcurrentLinkedQueue<>();
	private static final List<ValLink.Actor> ACTORS = new ArrayList<>();
	private static volatile boolean active;
	private static boolean installed;
	private static int ticks;
	private static long sentBytes, sentRaw;
	private static long nextLog;

	private GuestLink() {
	}

	/** Once: keep every ground message our Valheim sends; a guest also queues it for the host. */
	public static void install() {
		if (installed) {
			return;
		}
		installed = true;
		ValCollision.setForwarder((type, payload) -> {
			if (payload.length < 16) {
				return;
			}
			ByteBuffer b = ByteBuffer.wrap(payload).order(ByteOrder.nativeOrder());
			Key key = type == Proto.COL_TERRAIN ? new Key(type, b.getInt(0), b.getInt(4), 0) : new Key(type, b.getInt(0), b.getInt(4), b.getInt(8));
			List<byte[]> framed = split(type, payload);
			synchronized (CACHE) {
				CACHE.put(key, framed);
			}
			if (active) {
				OUTBOX.addAll(framed);
			}
		});
		// Our Valheim started over (another world, a dungeon): what we kept is from before.
		ValCollision.onCleared(() -> {
			synchronized (CACHE) {
				CACHE.clear();
			}
		});
		ClientPlayNetworking.registerGlobalReceiver(ValNet.Event.TYPE, (e, context) -> {
			if (ValLink.active()) {
				ValLink.pushEvent(e.kind(), e.formId(), e.a(), e.b(), e.c(), e.d(), e.flags(), e.weapon());
			}
		});
		ClientPlayNetworking.registerGlobalReceiver(ValNet.GroundAgain.TYPE, (p, context) -> {
			ValCraft.LOG.info("ValCraft: the host's ground was reset; sending ours again");
			resendAll();
		});
	}

	/** Busy ground (trees, rocks: a thousand triangles) is bigger than a packet: parts of up to PART_BYTES, the later ones added to the first. */
	private static final int PART_BYTES = 24000;

	private static List<byte[]> split(int type, byte[] payload) {
		int record = type == Proto.COL_REGION ? (int) Proto.COL_BLOCK_BYTES : type == Proto.COL_TRIS ? (int) Proto.COL_TRI_BYTES : 0;
		int header = (int) Proto.COL_REGION_HEADER_BYTES;
		if (record == 0 || payload.length <= PART_BYTES) {
			return List.of(frame(type, payload));
		}
		int count = (payload.length - header) / record;
		int per = (PART_BYTES - header) / record;
		List<byte[]> parts = new ArrayList<>();
		for (int first = 0; first < count; first += per) {
			int n = Math.min(per, count - first);
			ByteBuffer part = ByteBuffer.allocate(header + n * record).order(ByteOrder.nativeOrder());
			part.put(payload, 0, header);
			part.putInt(28, n);
			part.put(header, payload, header + first * record, n * record);
			parts.add(frame(first == 0 ? type : type | ValCollision.FORWARD_MORE, part.array()));
		}
		return parts;
	}

	private static byte[] frame(int type, byte[] payload) {
		ByteBuffer out = ByteBuffer.allocate(8 + payload.length).order(ByteOrder.nativeOrder());
		out.putInt(type).putInt(payload.length).put(payload);
		return out.array();
	}

	private static void resendAll() {
		synchronized (CACHE) {
			CACHE.values().forEach(OUTBOX::addAll);
		}
	}

	/** True while we're a guest in a ValCraft friend's world. */
	public static boolean active() {
		return active;
	}

	/** Every client tick while linked. */
	public static void tick(Minecraft minecraft) {
		install();
		boolean now = minecraft.getSingleplayerServer() == null && minecraft.player != null && minecraft.getConnection() != null
			&& ClientPlayNetworking.canSend(ValNet.Ground.TYPE);
		if (now != active) {
			active = now;
			OUTBOX.clear();
			ValCraft.LOG.info("ValCraft: multiplayer: {}", now ? "in a friend's ValCraft world; sharing our Valheim's ground and creatures with it" : "no longer a guest");
			if (now) {
				resendAll();
			}
		}
		if (!active) {
			return;
		}
		sendGround();
		if (++ticks % 2 == 0 && ValLink.readActors(ACTORS)) {
			List<ValLink.Actor> near = ACTORS.size() > ValNet.Actors.MAX ? new ArrayList<>(ACTORS.subList(0, ValNet.Actors.MAX)) : new ArrayList<>(ACTORS);
			ClientPlayNetworking.send(new ValNet.Actors(near));
		}
		if (System.currentTimeMillis() > nextLog && sentBytes > 0) {
			nextLog = System.currentTimeMillis() + 30_000;
			ValCraft.LOG.info("ValCraft: multiplayer: {} KB of ground sent to the host so far ({} KB unpacked)", sentBytes / 1024, sentRaw / 1024);
		}
	}

	private static void sendGround() {
		for (int packets = 0; packets < PACKETS_PER_TICK && !OUTBOX.isEmpty(); ) {
			List<byte[]> batch = new ArrayList<>();
			int raw = 0;
			byte[] next;
			while (raw < BATCH_RAW_BYTES && (next = OUTBOX.poll()) != null) {
				batch.add(next);
				raw += next.length;
			}
			packets += send(batch);
		}
	}

	/** Sends a batch in as few packets as fit; returns how many it took. */
	private static int send(List<byte[]> batch) {
		if (batch.isEmpty()) {
			return 0;
		}
		ByteArrayOutputStream raw = new ByteArrayOutputStream();
		for (byte[] m : batch) {
			raw.writeBytes(m);
		}
		byte[] packed = deflate(raw.toByteArray());
		if (packed.length <= ValNet.Ground.MAX_BYTES) {
			ClientPlayNetworking.send(new ValNet.Ground(packed));
			sentBytes += packed.length;
			sentRaw += raw.size();
			return 1;
		}
		if (batch.size() == 1) {
			ValCraft.LOG.warn("ValCraft: a piece of ground too big to send ({} bytes packed)", packed.length);
			return 0;
		}
		int half = batch.size() / 2;
		return send(batch.subList(0, half)) + send(batch.subList(half, batch.size()));
	}

	private static byte[] deflate(byte[] raw) {
		Deflater deflater = new Deflater(Deflater.BEST_SPEED);
		deflater.setInput(raw);
		deflater.finish();
		ByteArrayOutputStream out = new ByteArrayOutputStream(raw.length / 4 + 64);
		byte[] buf = new byte[32768];
		while (!deflater.finished()) {
			out.write(buf, 0, deflater.deflate(buf));
		}
		deflater.end();
		return out.toByteArray();
	}
}
