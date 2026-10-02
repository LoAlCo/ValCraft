package dev.valcraft.world;

import dev.valcraft.ValCraft;
import it.unimi.dsi.fastutil.longs.Long2ObjectOpenHashMap;
import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.Iterator;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerChunkEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.fabricmc.fabric.api.event.player.PlayerBlockBreakEvents;
import net.fabricmc.fabric.api.event.player.UseBlockCallback;
import net.fabricmc.fabric.api.networking.v1.ServerPlayConnectionEvents;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.Registries;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.nbt.LongArrayTag;
import net.minecraft.nbt.NbtAccounter;
import net.minecraft.nbt.NbtIo;
import net.minecraft.nbt.NbtUtils;
import net.minecraft.nbt.Tag;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.ProblemReporter;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.BedBlock;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.entity.BlockEntity;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;
import net.minecraft.world.level.block.state.properties.DoubleBlockHalf;
import net.minecraft.world.level.chunk.LevelChunk;
import net.minecraft.world.level.chunk.LevelChunkSection;
import net.minecraft.world.level.storage.LevelResource;
import net.minecraft.world.level.storage.TagValueInput;
import org.jspecify.annotations.Nullable;

/**
 * Builds follow across F8. Each Valheim world has two Minecraft saves, ValCraft-&lt;id&gt;
 * (Valheim terrain) and ValCraft-&lt;id&gt;-blocks (block terrain); what's built or broken in one
 * shows up in the other.
 *
 * Every block a player places, breaks, pours a bucket into or uses (doors, chests, levers...) is
 * noted; on leaving, every 30 s and on shutdown, the final state of each noted spot (with its
 * block entity: chest contents, sign text) is queued for the other save, in
 * ValCraft-&lt;id&gt;.builds.dat next to both. The queue for the open save is applied as its chunks
 * load, and in the block save only once block terrain has built the chunk (or the terrain would
 * bury the build). The same spot changed in both: the later change wins.
 *
 * Builds from before this existed: the Valheim-terrain save is a void, so every block in it is
 * the player's; each of its chunks is exported once, the first time it loads.
 */
public final class BuildSync {
	private static final int SAVE_EVERY_TICKS = 30 * 20;
	private static final int APPLY_PER_TICK = 1024;
	private static final int APPLY_FLAGS = Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SUPPRESS_DROPS;

	// The open save's state (server thread only).
	private static @Nullable MinecraftServer server;
	private static @Nullable Path file;
	private static boolean blocksSave;
	/** Spots changed here since the last flush. */
	private static final LongOpenHashSet TOUCHED = new LongOpenHashSet();
	/** Changes made in the other save, to apply here. */
	private static final Long2ObjectOpenHashMap<CompoundTag> INCOMING = new Long2ObjectOpenHashMap<>();
	/** Changes made here, for the other save. */
	private static final Long2ObjectOpenHashMap<CompoundTag> OUTGOING = new Long2ObjectOpenHashMap<>();
	/** Valheim-terrain chunks whose old builds have been exported. */
	private static final LongOpenHashSet EXPORTED = new LongOpenHashSet();
	private static boolean dirty;
	/** Valheim was told builds are on their way (it says so on screen) and not yet that they're in. */
	private static boolean announced;
	/** Only right after opening a save (F8): later, while travelling, builds just appear quietly. */
	private static int openedAtTick;
	private static final int ANNOUNCE_TICKS = 60 * 20;
	private static int ticks;

	private BuildSync() {
	}

	public static void init() {
		ServerLifecycleEvents.SERVER_STARTED.register(BuildSync::open);
		ServerLifecycleEvents.SERVER_STOPPING.register(s -> close());
		ServerPlayConnectionEvents.DISCONNECT.register((handler, s) -> flush());
		ServerTickEvents.END_SERVER_TICK.register(BuildSync::tick);
		PlayerBlockBreakEvents.AFTER.register((level, player, pos, state, blockEntity) -> touch(level, pos, state));
		UseBlockCallback.EVENT.register((player, level, hand, hit) -> {
			BlockState state = level.getBlockState(hit.getBlockPos());
			// Only things using changes (a door opening, a chest's contents): never terrain.
			if (state.hasBlockEntity() || state.hasProperty(BlockStateProperties.OPEN) || state.hasProperty(BlockStateProperties.POWERED)) {
				touch(level, hit.getBlockPos(), state);
			}
			return InteractionResult.PASS;
		});
		ServerChunkEvents.CHUNK_LOAD.register((level, chunk, generated) -> exportOldBuilds(level, chunk));
	}

	// ---- the open save ------------------------------------------------------------------------

	private static void open(MinecraftServer s) {
		close();
		Path root = s.getWorldPath(LevelResource.ROOT).toAbsolutePath().normalize();
		String name = root.getFileName().toString();
		if (!name.startsWith(ValCraft.WORLD_NAME)) {
			return;
		}
		blocksSave = name.endsWith("-blocks");
		String pair = blocksSave ? name.substring(0, name.length() - "-blocks".length()) : name;
		server = s;
		openedAtTick = s.getTickCount();
		file = root.getParent().resolve(pair + ".builds.dat");
		read();
		ValCraft.LOG.info("ValCraft: build sync: {} changes from the other terrain mode to apply here", INCOMING.size());
	}

	private static void close() {
		if (server != null) {
			flush();
		}
		server = null;
		file = null;
		TOUCHED.clear();
		INCOMING.clear();
		OUTGOING.clear();
		EXPORTED.clear();
		dirty = false;
		announced = false;
	}

	private static String incomingKey() {
		return blocksSave ? "toBlocks" : "toValheim";
	}

	private static String outgoingKey() {
		return blocksSave ? "toValheim" : "toBlocks";
	}

	private static void read() {
		if (file == null || !Files.exists(file)) {
			return;
		}
		try {
			CompoundTag all = NbtIo.readCompressed(file, NbtAccounter.unlimitedHeap());
			readList(all, incomingKey(), INCOMING);
			readList(all, outgoingKey(), OUTGOING);
			all.getLongArray("exported").ifPresent(a -> {
				for (long k : a) {
					EXPORTED.add(k);
				}
			});
		} catch (IOException | RuntimeException e) {
			ValCraft.LOG.warn("ValCraft: couldn't read {}", file, e);
		}
	}

	private static void readList(CompoundTag all, String key, Long2ObjectOpenHashMap<CompoundTag> into) {
		for (Tag t : all.getListOrEmpty(key)) {
			if (t instanceof CompoundTag e) {
				e.getLong("pos").ifPresent(p -> into.put((long) p, e));
			}
		}
	}

	private static void write() {
		if (file == null || !dirty) {
			return;
		}
		try {
			CompoundTag all = new CompoundTag();
			all.put(incomingKey(), list(INCOMING));
			all.put(outgoingKey(), list(OUTGOING));
			all.put("exported", new LongArrayTag(EXPORTED.toLongArray()));
			Path tmp = file.resolveSibling(file.getFileName() + ".tmp");
			NbtIo.writeCompressed(all, tmp);
			Files.move(tmp, file, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
			dirty = false;
		} catch (IOException | RuntimeException e) {
			ValCraft.LOG.warn("ValCraft: couldn't write {}", file, e);
		}
	}

	private static ListTag list(Long2ObjectOpenHashMap<CompoundTag> map) {
		ListTag l = new ListTag();
		l.addAll(map.values());
		return l;
	}

	// ---- noting changes -----------------------------------------------------------------------

	/** Commands running on the server thread (nested: /execute, functions). */
	private static int commandDepth;

	public static void commandStarted() {
		commandDepth++;
	}

	public static void commandEnded() {
		commandDepth--;
	}

	/** A command is running: the blocks it sets are the player's doing (/fill, /setblock, /clone). */
	public static boolean inCommand() {
		return commandDepth > 0 && server != null && server.isSameThread();
	}

	/** A player changed the block at pos (state: what was there, for doors' and beds' other half). */
	public static void touch(Level level, BlockPos pos, BlockState state) {
		if (server == null || !(level instanceof ServerLevel sl) || sl != server.overworld()) {
			return;
		}
		TOUCHED.add(pos.asLong());
		BlockPos other = otherHalf(pos, state);
		if (other != null) {
			TOUCHED.add(other.asLong());
		}
	}

	private static @Nullable BlockPos otherHalf(BlockPos pos, BlockState state) {
		if (state.hasProperty(BlockStateProperties.DOUBLE_BLOCK_HALF)) {
			return state.getValue(BlockStateProperties.DOUBLE_BLOCK_HALF) == DoubleBlockHalf.LOWER ? pos.above() : pos.below();
		}
		if (state.hasProperty(BlockStateProperties.BED_PART)) {
			return pos.relative(BedBlock.getConnectedDirection(state));
		}
		return null;
	}

	/** The noted spots' current state goes to the other save. */
	private static void flush() {
		if (server == null) {
			return;
		}
		ServerLevel level = server.overworld();
		var it = TOUCHED.iterator();
		while (it.hasNext()) {
			long p = it.nextLong();
			OUTGOING.put(p, entry(level, BlockPos.of(p)));
			INCOMING.remove(p);  // changed here after the other save did: this one is newer
		}
		if (!TOUCHED.isEmpty()) {
			dirty = true;
		}
		TOUCHED.clear();
		write();
	}

	private static CompoundTag entry(ServerLevel level, BlockPos pos) {
		return entry(level, pos, level.getBlockState(pos), level.getBlockEntity(pos));
	}

	private static CompoundTag entry(ServerLevel level, BlockPos pos, BlockState state, @Nullable BlockEntity be) {
		CompoundTag e = new CompoundTag();
		e.putLong("pos", pos.asLong());
		e.put("state", NbtUtils.writeBlockState(state));
		if (be != null) {
			e.put("be", be.saveWithFullMetadata(level.registryAccess()));
		}
		return e;
	}

	// ---- applying the other save's changes ---------------------------------------------------

	private static void tick(MinecraftServer s) {
		if (server != s) {
			return;
		}
		if (++ticks >= SAVE_EVERY_TICKS) {
			ticks = 0;
			flush();
		}
		if (INCOMING.isEmpty()) {
			announce(false);
			return;
		}
		if (s.getTickCount() % 4 != 0) {
			return;  // five times a second is plenty (the queue can hold thousands of far-off changes)
		}
		ServerLevel level = s.overworld();
		var lookup = level.holderLookup(Registries.BLOCK);
		int applied = 0, waiting = 0;
		Iterator<Long2ObjectOpenHashMap.Entry<CompoundTag>> it = INCOMING.long2ObjectEntrySet().fastIterator();
		while (it.hasNext() && applied < APPLY_PER_TICK) {
			var en = it.next();
			BlockPos pos = BlockPos.of(en.getLongKey());
			if (!level.isLoaded(pos)) {
				continue;  // later, when the player gets there
			}
			if (blocksSave && !TerrainGen.isBuilt(pos.getX() >> 4, pos.getZ() >> 4)) {
				waiting++;  // here, but its terrain isn't built yet
				continue;
			}
			CompoundTag e = en.getValue();
			try {
				BlockState state = NbtUtils.readBlockState(lookup, e.getCompoundOrEmpty("state"));
				level.setBlock(pos, state, APPLY_FLAGS);
				e.getCompound("be").ifPresent(tag -> {
					BlockEntity be = level.getBlockEntity(pos);
					if (be != null) {
						be.loadWithComponents(TagValueInput.create(ProblemReporter.DISCARDING, level.registryAccess(), tag));
						be.setChanged();
						level.sendBlockUpdated(pos, state, state, Block.UPDATE_CLIENTS);
					}
				});
			} catch (RuntimeException ex) {
				ValCraft.LOG.warn("ValCraft: couldn't apply a synced block at {}", pos, ex);
			}
			it.remove();
			applied++;
		}
		if (applied > 0) {
			dirty = true;
			announce(true);
		} else if (waiting == 0) {
			announce(false);  // nothing near the player left to come
		} else {
			announce(true);
		}
	}

	/** Tells Valheim (which shows its own message) that builds are arriving, or that they're in. */
	private static void announce(boolean arriving) {
		if (arriving == announced || arriving && server != null && server.getTickCount() - openedAtTick > ANNOUNCE_TICKS) {
			return;
		}
		announced = arriving;
		dev.valcraft.link.ValLink.pushEvent(dev.valcraft.link.Proto.EV_BUILD_SYNC, arriving ? 1 : 0, 0, 0, 0, 0, 0);
	}

	// ---- builds from before ---------------------------------------------------------------------

	/** Valheim-terrain save: a chunk's blocks (all the player's: it's a void) go to the block save, once. */
	private static void exportOldBuilds(ServerLevel level, LevelChunk chunk) {
		if (server == null || blocksSave || level != server.overworld()) {
			return;
		}
		long key = chunk.getPos().pack();
		if (!EXPORTED.add(key)) {
			return;
		}
		dirty = true;
		int baseX = chunk.getPos().getMinBlockX(), baseZ = chunk.getPos().getMinBlockZ();
		LevelChunkSection[] sections = chunk.getSections();
		BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
		for (int i = 0; i < sections.length; i++) {
			LevelChunkSection section = sections[i];
			if (section == null || section.hasOnlyAir()) {
				continue;
			}
			int baseY = level.getSectionYFromSectionIndex(i) << 4;
			for (int y = 0; y < 16; y++) {
				for (int z = 0; z < 16; z++) {
					for (int x = 0; x < 16; x++) {
						BlockState state = section.getBlockState(x, y, z);
						if (state.isAir()) {
							continue;
						}
						pos.set(baseX + x, baseY + y, baseZ + z);
						long p = pos.asLong();
						if (!OUTGOING.containsKey(p)) {
							// From the chunk itself: asking the level would wait for this very chunk,
							// which is still loading (that froze the server).
							BlockPos at = pos.immutable();
							OUTGOING.put(p, entry(level, at, state, chunk.getBlockEntity(at)));
						}
					}
				}
			}
		}
	}
}
