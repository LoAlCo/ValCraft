package dev.valcraft.world;

import static dev.valcraft.link.Proto.*;
import static java.lang.foreign.ValueLayout.*;

import dev.valcraft.ValCraft;
import dev.valcraft.link.ValLink;
import java.lang.foreign.MemorySegment;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import net.minecraft.core.BlockPos;
import net.minecraft.world.phys.shapes.BitSetDiscreteVoxelShape;
import net.minecraft.world.phys.shapes.CubeVoxelShape;
import net.minecraft.world.phys.shapes.Shapes;
import net.minecraft.world.phys.shapes.VoxelShape;
import org.jspecify.annotations.Nullable;

/**
 * Valheim's world geometry as Minecraft sees it: an 8x8x8 sub-voxel collision shape per block
 * position, streamed from the SKSE plugin. These are not blocks; they are merged into block
 * collision queries (see BlockCollisionsMixin) so vanilla movement code collides with them.
 */
public final class ValCollision {
	/** Valheim regions are streamed as cubes of this many blocks. Must match the SKSE side. */
	public static final int REGION_SIZE = 8;

	private static final ConcurrentHashMap<Long, VoxelShape> SHAPES = new ConcurrentHashMap<>();
	/** Valheim's terrain surface per chunk, for mobs: 256 columns' surface block y, then 256 fractions (0..255). */
	private static final ConcurrentHashMap<Long, short[]> TERRAIN = new ConcurrentHashMap<>();
	// Per block: sub-voxel count (bits 0-9), any in the lower half (bit 10), any in the upper half (bit 11).
	private static final ConcurrentHashMap<Long, Integer> FILL = new ConcurrentHashMap<>();
	private static final int FILL_LOWER = 1 << 10;
	private static final int FILL_UPPER = 1 << 11;
	private static final int FILL_TOP_SHIFT = 12; // highest occupied of the 8 voxel layers (3 bits)
	private static final ConcurrentHashMap<Long, ValTri[]> TRIS = new ConcurrentHashMap<>();
	private static volatile java.util.function.Predicate<net.minecraft.world.entity.Entity> smoothCollider = e -> false;
	private static final Set<Long> KNOWN_REGIONS = ConcurrentHashMap.newKeySet();
	private static volatile int epoch = -1;
	private static Thread consumer;
	// Multiplayer: a guest's client hands its Valheim's ground on to the host's server (see GuestLink).
	private static volatile java.util.function.@Nullable BiConsumer<Integer, byte[]> forwarder;
	private static final java.util.List<Runnable> ON_CLEARED = new java.util.concurrent.CopyOnWriteArrayList<>();

	private ValCollision() {
	}

	public static @Nullable VoxelShape shapeAt(BlockPos pos) {
		return SHAPES.isEmpty() ? null : SHAPES.get(pos.asLong());
	}

	/** Entities (the local player) that collide with Valheim's exact triangles instead of its voxels. */
	public static void setSmoothCollider(java.util.function.Predicate<net.minecraft.world.entity.Entity> predicate) {
		smoothCollider = predicate;
	}

	public static boolean usesSmoothCollider(net.minecraft.world.entity.@Nullable Entity entity) {
		return entity != null && smoothCollider.test(entity);
	}

	/** Adds every Valheim triangle whose bounds overlap {@code box}. */
	public static void trianglesNear(net.minecraft.world.phys.AABB box, java.util.List<ValTri> out) {
		if (TRIS.isEmpty()) {
			return;
		}
		int rx0 = Math.floorDiv((int) Math.floor(box.minX), REGION_SIZE), rx1 = Math.floorDiv((int) Math.floor(box.maxX), REGION_SIZE);
		int ry0 = Math.floorDiv((int) Math.floor(box.minY), REGION_SIZE), ry1 = Math.floorDiv((int) Math.floor(box.maxY), REGION_SIZE);
		int rz0 = Math.floorDiv((int) Math.floor(box.minZ), REGION_SIZE), rz1 = Math.floorDiv((int) Math.floor(box.maxZ), REGION_SIZE);
		for (int rx = rx0; rx <= rx1; rx++) {
			for (int ry = ry0; ry <= ry1; ry++) {
				for (int rz = rz0; rz <= rz1; rz++) {
					ValTri[] tris = TRIS.get(regionKey(rx, ry, rz));
					if (tris == null) {
						continue;
					}
					for (ValTri t : tris) {
						if (t.maxX >= box.minX && t.minX <= box.maxX && t.maxY >= box.minY && t.minY <= box.maxY && t.maxZ >= box.minZ && t.minZ <= box.maxZ) {
							out.add(t);
						}
					}
				}
			}
		}
	}

	/** True once Valheim has sent the region containing this block (even if it was empty). */
	public static boolean isKnown(int x, int y, int z) {
		return KNOWN_REGIONS.contains(regionKey(Math.floorDiv(x, REGION_SIZE), Math.floorDiv(y, REGION_SIZE), Math.floorDiv(z, REGION_SIZE)));
	}

	/** True if any Valheim geometry exists in the 3x3 column below (x, y, z), down to {@code depth} blocks. */
	public static boolean hasSolidBelow(int x, int y, int z, int depth) {
		for (int dy = 0; dy <= depth; dy++) {
			for (int dx = -1; dx <= 1; dx++) {
				for (int dz = -1; dz <= 1; dz++) {
					if (SHAPES.containsKey(BlockPos.asLong(x + dx, y - dy, z + dz))) {
						return true;
					}
				}
			}
		}
		return false;
	}

	/** Fraction (0..1) of this block's volume that is Valheim geometry. */
	public static float solidFraction(BlockPos pos) {
		Integer fill = FILL.isEmpty() ? null : FILL.get(pos.asLong());
		return fill == null ? 0.0F : (fill & 0x3FF) / 512.0F;
	}

	/** True if any Valheim geometry is in this cell. */
	public static boolean hasGeometry(BlockPos pos) {
		return !FILL.isEmpty() && FILL.containsKey(pos.asLong());
	}

	/**
	 * How high (0..1) Valheim geometry reaches in this cell: the top of its highest part. Terrain
	 * arrives as a thin surface, so what lies below that surface counts as ground too.
	 */
	public static float groundTop(BlockPos pos) {
		Integer fill = FILL.isEmpty() ? null : FILL.get(pos.asLong());
		return fill == null ? 0.0F : (((fill >> FILL_TOP_SHIFT) & 7) + 1) / 8.0F;
	}

	/** True if Valheim ground holds up whatever is in this cell (terrain in its lower half or the top of the cell below). */
	public static boolean supportsFromBelow(BlockPos pos) {
		if (FILL.isEmpty()) {
			return false;
		}
		Integer here = FILL.get(pos.asLong());
		if (here != null && (here & FILL_LOWER) != 0) {
			return true;
		}
		Integer below = FILL.get(BlockPos.asLong(pos.getX(), pos.getY() - 1, pos.getZ()));
		return below != null && (below & FILL_UPPER) != 0;
	}

	public static int blockCount() {
		return SHAPES.size();
	}

	public static int regionCount() {
		return KNOWN_REGIONS.size();
	}

	/** Valheim is describing its world around the player (false in a plain Minecraft world). */
	public static boolean active() {
		return !KNOWN_REGIONS.isEmpty();
	}

	private static long regionKey(int rx, int ry, int rz) {
		return BlockPos.asLong(rx, ry, rz);
	}

	public static synchronized void startConsumer() {
		if (consumer != null) {
			return;
		}
		consumer = new Thread(ValCollision::consumeLoop, "ValCraft collision");
		consumer.setDaemon(true);
		consumer.start();
	}

	private static void consumeLoop() {
		while (true) {
			try {
				if (!drainOnce()) {
					Thread.sleep(2);
				}
			} catch (InterruptedException e) {
				return;
			} catch (Throwable t) {
				ValCraft.LOG.error("ValCraft: collision consumer error", t);
				try {
					Thread.sleep(500);
				} catch (InterruptedException e) {
					return;
				}
			}
		}
	}

	/** Processes all pending collision messages. Returns true if anything was consumed. */
	private static boolean drainOnce() {
		MemorySegment s = ValLink.segment();
		if (s == null) {
			return false;
		}
		long head = ValLink.collisionHead();
		long tail = ValLink.collisionTail();
		if (tail >= head) {
			return false;
		}
		long data = OFF_COLLISION_RING + CR_DATA;
		while (tail < head) {
			long pos = tail % CR_DATA_BYTES;
			int type = s.get(JAVA_INT, data + pos);
			int payloadBytes = s.get(JAVA_INT, data + pos + 4);
			if (type == COL_PAD) {
				tail += CR_DATA_BYTES - pos;
				continue;
			}
			long payload = data + pos + 8;
			var fwd = forwarder;
			if (fwd != null && (type == COL_REGION || type == COL_TRIS || type == COL_TERRAIN)) {
				fwd.accept(type, s.asSlice(payload, payloadBytes).toArray(JAVA_BYTE));
			}
			switch (type) {
				case COL_CLEAR -> clear(s.get(JAVA_INT, payload));
				case COL_REGION -> readRegion(s, payload, false);
				case COL_TRIS -> readTris(s, payload, false);
				case COL_TERRAIN -> readTerrain(s, payload);
				default -> ValCraft.LOG.warn("ValCraft: unknown collision message {}", type);
			}
			tail += align8(8 + payloadBytes);
		}
		ValLink.setCollisionTail(tail);
		return true;
	}

	private static long align8(long v) {
		return (v + 7) & ~7L;
	}

	/** A freshly started client joins whatever collision epoch Valheim is already on. */
	private static void adoptEpochIfFresh(int msgEpoch) {
		if (epoch == -1) {
			epoch = msgEpoch;
			ValCraft.LOG.info("ValCraft: joined collision epoch {} already in progress", msgEpoch);
		}
	}

	private static void clear(int newEpoch) {
		SHAPES.clear();
		TERRAIN.clear();
		FILL.clear();
		TRIS.clear();
		KNOWN_REGIONS.clear();
		epoch = newEpoch;
		ValCraft.LOG.info("ValCraft: collision cleared (epoch {})", newEpoch);
		for (Runnable cleared : ON_CLEARED) {
			cleared.run();
		}
	}

	/** Multiplayer, guest side: every ground message our Valheim sends also goes here (null: stop). */
	public static void setForwarder(java.util.function.@Nullable BiConsumer<Integer, byte[]> fwd) {
		forwarder = fwd;
	}

	/** A forwarded region's later parts (it was split to fit a packet) carry this in their type. */
	public static final int FORWARD_MORE = 0x100;

	/** Multiplayer: run when our Valheim wipes its ground (a host: the guests' went with it; a guest: what it forwarded is old). */
	public static void onCleared(Runnable run) {
		ON_CLEARED.add(run);
	}

	/**
	 * Multiplayer, host side: a guest's ground (GuestLink's batch: deflated [type, length, payload]...),
	 * added to ours. Both Valheims describe the same world; the guest's pieces take this epoch.
	 */
	public static void receiveForwarded(byte[] deflated) {
		if (epoch == -1) {
			return; // our own Valheim hasn't described anything yet
		}
		byte[] raw;
		try {
			var inflater = new java.util.zip.Inflater();
			inflater.setInput(deflated);
			var out = new java.io.ByteArrayOutputStream(deflated.length * 4);
			byte[] buf = new byte[65536];
			while (!inflater.finished()) {
				int n = inflater.inflate(buf);
				if (n == 0 && (inflater.needsInput() || inflater.needsDictionary())) {
					break;
				}
				out.write(buf, 0, n);
				if (out.size() > (8 << 20)) {
					break;
				}
			}
			inflater.end();
			raw = out.toByteArray();
		} catch (java.util.zip.DataFormatException e) {
			ValCraft.LOG.warn("ValCraft: a guest's ground didn't unpack", e);
			return;
		}
		var in = java.nio.ByteBuffer.wrap(raw).order(java.nio.ByteOrder.nativeOrder());
		while (in.remaining() >= 8) {
			int type = in.getInt();
			int len = in.getInt();
			if (len < 32 || len > in.remaining()) {
				break;
			}
			// An 8-aligned copy: the readers use aligned layouts.
			long[] words = new long[(len + 7) / 8];
			MemorySegment seg = MemorySegment.ofArray(words);
			MemorySegment.copy(MemorySegment.ofArray(raw), in.position(), seg, 0, len);
			in.position(in.position() + len);
			try {
				boolean more = (type & FORWARD_MORE) != 0;
				switch (type & ~FORWARD_MORE) {
					case COL_REGION -> {
						seg.set(JAVA_INT, 24, epoch);
						readRegion(seg, 0, more);
					}
					case COL_TRIS -> {
						seg.set(JAVA_INT, 24, epoch);
						readTris(seg, 0, more);
					}
					case COL_TERRAIN -> readTerrain(seg, 0);
					default -> {
					}
				}
			} catch (IndexOutOfBoundsException e) {
				ValCraft.LOG.warn("ValCraft: a guest sent a broken ground message ({})", type);
				return;
			}
		}
	}

	/** {@code more}: another part of a region already started (a guest's, split to fit a packet): add to it. */
	private static void readRegion(MemorySegment s, long p, boolean more) {
		int minX = s.get(JAVA_INT, p);
		int minY = s.get(JAVA_INT, p + 4);
		int minZ = s.get(JAVA_INT, p + 8);
		int maxX = s.get(JAVA_INT, p + 12);
		int maxY = s.get(JAVA_INT, p + 16);
		int maxZ = s.get(JAVA_INT, p + 20);
		int msgEpoch = s.get(JAVA_INT, p + 24);
		int count = s.get(JAVA_INT, p + 28);
		adoptEpochIfFresh(msgEpoch);
		if (msgEpoch != epoch) {
			return; // stale region from before a world change
		}

		// Build the new shapes first so readers never see a half-empty region.
		java.util.HashMap<Long, VoxelShape> fresh = new java.util.HashMap<>(count * 2);
		java.util.HashMap<Long, Integer> freshFill = new java.util.HashMap<>(count * 2);
		long e = p + COL_REGION_HEADER_BYTES;
		for (int i = 0; i < count; i++, e += COL_BLOCK_BYTES) {
			int x = s.get(JAVA_INT, e);
			int y = s.get(JAVA_INT, e + 4);
			int z = s.get(JAVA_INT, e + 8);
			VoxelShape shape = buildShape(s, e + 16);
			if (shape != null) {
				long key = BlockPos.asLong(x, y, z);
				fresh.put(key, shape);
				freshFill.put(key, fillInfo(s, e + 16));
			}
		}

		if (more) {
			SHAPES.putAll(fresh);
			FILL.putAll(freshFill);
			return;
		}
		for (int x = minX; x <= maxX; x++) {
			for (int y = minY; y <= maxY; y++) {
				for (int z = minZ; z <= maxZ; z++) {
					long key = BlockPos.asLong(x, y, z);
					VoxelShape shape = fresh.get(key);
					if (shape != null) {
						SHAPES.put(key, shape);
						FILL.put(key, freshFill.get(key));
					} else {
						SHAPES.remove(key);
						FILL.remove(key);
					}
				}
			}
		}

		for (int rx = Math.floorDiv(minX, REGION_SIZE); rx <= Math.floorDiv(maxX, REGION_SIZE); rx++) {
			for (int ry = Math.floorDiv(minY, REGION_SIZE); ry <= Math.floorDiv(maxY, REGION_SIZE); ry++) {
				for (int rz = Math.floorDiv(minZ, REGION_SIZE); rz <= Math.floorDiv(maxZ, REGION_SIZE); rz++) {
					KNOWN_REGIONS.add(regionKey(rx, ry, rz));
				}
			}
		}
	}

	private static void readTris(MemorySegment s, long p, boolean more) {
		int minX = s.get(JAVA_INT, p);
		int minY = s.get(JAVA_INT, p + 4);
		int minZ = s.get(JAVA_INT, p + 8);
		int msgEpoch = s.get(JAVA_INT, p + 24);
		int count = s.get(JAVA_INT, p + 28);
		adoptEpochIfFresh(msgEpoch);
		if (msgEpoch != epoch) {
			return;
		}
		ValTri[] tris = new ValTri[count];
		float[] v = new float[9];
		int kept = 0;
		long e = p + COL_REGION_HEADER_BYTES;
		for (int i = 0; i < count; i++, e += COL_TRI_BYTES) {
			for (int k = 0; k < 9; k++) {
				v[k] = s.get(JAVA_FLOAT, e + k * 4L);
			}
			int flags = s.get(JAVA_INT, e + 36);
			ValTri t = new ValTri(v, 0, (flags & TRI_STAIR_HELPER) != 0);
			if (!t.degenerate()) {
				tris[kept++] = t;
			}
		}
		long key = regionKey(Math.floorDiv(minX, REGION_SIZE), Math.floorDiv(minY, REGION_SIZE), Math.floorDiv(minZ, REGION_SIZE));
		ValTri[] had = more ? TRIS.get(key) : null;
		if (had != null) {
			ValTri[] all = java.util.Arrays.copyOf(had, had.length + kept);
			System.arraycopy(tris, 0, all, had.length, kept);
			TRIS.put(key, all);
		} else {
			TRIS.put(key, java.util.Arrays.copyOf(tris, kept));
		}
	}

	/** cx, cz, epoch, pad; then 256 columns [x + 16 z] of {short top, byte biome, byte flags}. */
	private static void readTerrain(MemorySegment s, long p) {
		int cx = s.get(JAVA_INT, p), cz = s.get(JAVA_INT, p + 4);
		boolean floors = (s.get(JAVA_INT, p + 12) & 1) != 0;
		short[] top = new short[256];
		byte[] biome = new byte[256];
		byte[] floor = new byte[256];
		short[] surface = new short[768]; // top, then fraction, then biome
		for (int i = 0; i < 256; i++) {
			long c = p + 16 + i * 4L;
			top[i] = s.get(JAVA_SHORT, c);
			biome[i] = s.get(JAVA_BYTE, c + 2);
			surface[i] = top[i];
			surface[256 + i] = (short) (s.get(JAVA_BYTE, c + 3) & 0xFF);
			surface[512 + i] = biome[i];
			// how far under top the bedrock is (Valheim's dig limit); 12 from a Valheim side without it
			floor[i] = floors ? s.get(JAVA_BYTE, p + 16 + 256 * 4L + i) : (byte) 12;
		}
		TERRAIN.put(((long) cx << 32) ^ (cz & 0xFFFFFFFFL), surface);
		TerrainGen.receive(cx, cz, top, biome, floor);
	}

	/** The y of Valheim's terrain surface block at this column, or Integer.MIN_VALUE when not known. */
	public static int terrainTop(int x, int z) {
		short[] top = TERRAIN.get(((long) (x >> 4) << 32) ^ ((z >> 4) & 0xFFFFFFFFL));
		return top == null ? Integer.MIN_VALUE : top[(x & 15) + 16 * (z & 15)];
	}

	/** The Valheim biome (TerrainGen.BIOME_*) at this column, or 0 when not known. */
	public static int terrainBiome(int x, int z) {
		short[] t = TERRAIN.get(((long) (x >> 4) << 32) ^ ((z >> 4) & 0xFFFFFFFFL));
		return t == null ? 0 : t[512 + (x & 15) + 16 * (z & 15)];
	}

	/** The exact height of Valheim's terrain surface at this column, or NaN when not known. */
	public static float terrainHeight(int x, int z) {
		short[] t = TERRAIN.get(((long) (x >> 4) << 32) ^ ((z >> 4) & 0xFFFFFFFFL));
		if (t == null) {
			return Float.NaN;
		}
		int i = (x & 15) + 16 * (z & 15);
		return t[i] + 0.5F + t[256 + i] / 255.0F;
	}

	/**
	 * Where Valheim hasn't sent its detailed collision (beyond what's around the player), the terrain
	 * surface stands in for it, so mobs wandering off don't drop out of the world. Null elsewhere.
	 */
	public static @Nullable VoxelShape terrainShapeAt(BlockPos pos) {
		if (TERRAIN.isEmpty() || isKnown(pos.getX(), pos.getY(), pos.getZ())) {
			return null;
		}
		float h = terrainHeight(pos.getX(), pos.getZ());
		if (Float.isNaN(h) || pos.getY() > h) {
			return null;
		}
		float up = Math.min(1.0F, h - pos.getY());
		return up >= 0.999F ? Shapes.block() : up > 0.01F ? Shapes.box(0, 0, 0, 1, up, 1) : null;
	}

	public static int triangleCount() {
		int n = 0;
		for (ValTri[] t : TRIS.values()) {
			n += t.length;
		}
		return n;
	}

	private static int fillInfo(MemorySegment s, long bitsOff) {
		int count = 0;
		int info = 0;
		int top = 0;
		for (int y = 0; y < 8; y++) {
			long layer = s.get(JAVA_LONG, bitsOff + y * 8L);
			count += Long.bitCount(layer);
			if (layer != 0) {
				info |= y < 4 ? FILL_LOWER : FILL_UPPER;
				top = y;
			}
		}
		return info | count | top << FILL_TOP_SHIFT;
	}

	private static @Nullable VoxelShape buildShape(MemorySegment s, long bitsOff) {
		boolean any = false;
		boolean full = true;
		long[] layers = new long[8];
		for (int y = 0; y < 8; y++) {
			layers[y] = s.get(JAVA_LONG, bitsOff + y * 8L);
			any |= layers[y] != 0;
			full &= layers[y] == -1L;
		}
		if (!any) {
			return null;
		}
		if (full) {
			return Shapes.block();
		}
		BitSetDiscreteVoxelShape discrete = new BitSetDiscreteVoxelShape(8, 8, 8);
		for (int y = 0; y < 8; y++) {
			long layer = layers[y];
			while (layer != 0) {
				int bit = Long.numberOfTrailingZeros(layer);
				layer &= layer - 1;
				discrete.fill(bit & 7, y, bit >>> 3);
			}
		}
		return new CubeVoxelShape(discrete);
	}
}
