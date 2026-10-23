package dev.valcraft.link;

/**
 * What Valheim's inventory tabs show, for ValCraft's Valheim-style UI (COL_VALHEIM_INFO, from UiSync.cs):
 * the latest message as it came, for the client to read. Valheim sends it when it changes, and all of
 * it again now and then.
 */
public final class ValheimInfo {
	private static volatile byte[] latest;
	private static volatile int seq;

	private ValheimInfo() {
	}

	/** Whichever thread reads the collision ring. */
	public static void receive(byte[] message) {
		latest = message;
		seq++;
	}

	public static byte[] latest() {
		return latest;
	}

	/** Changes with every message. */
	public static int seq() {
		return seq;
	}
}
