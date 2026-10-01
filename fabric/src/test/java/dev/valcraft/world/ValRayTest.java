package dev.valcraft.world;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;

import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;

class ValRayTest {
	private static ValTri tri(boolean stairHelper, double... v) {
		float[] f = new float[9];
		for (int i = 0; i < 9; i++) {
			f[i] = (float) v[i];
		}
		return new ValTri(f, 0, stairHelper);
	}

	/** Flat ground at height y covering [-10, 10] in x and z. */
	private static List<ValTri> ground(double y) {
		List<ValTri> out = new ArrayList<>();
		out.add(tri(false, -10, y, -10, 10, y, -10, 10, y, 10));
		out.add(tri(false, -10, y, -10, 10, y, 10, -10, y, 10));
		return out;
	}

	@Test
	void hitsGroundFromAbove() {
		ValRay.Hit hit = ValRay.cast(ground(10.3), 0.5, 12.0, 0.5, 0.5, 8.0, 0.5);
		assertNotNull(hit);
		assertEquals(10.3, hit.y(), 1e-5);
		assertEquals(1.0, hit.ny(), 1e-9); // normal faces the ray origin
	}

	@Test
	void missesWhenSegmentStopsShort() {
		assertNull(ValRay.cast(ground(10.3), 0.5, 12.0, 0.5, 0.5, 11.0, 0.5));
	}

	@Test
	void normalFacesRayFromBelow() {
		ValRay.Hit hit = ValRay.cast(ground(10.3), 0.5, 8.0, 0.5, 0.5, 12.0, 0.5);
		assertNotNull(hit);
		assertEquals(-1.0, hit.ny(), 1e-9);
	}

	@Test
	void picksNearestOfSeveralSurfaces() {
		List<ValTri> tris = ground(10.0);
		tris.addAll(ground(5.0));
		ValRay.Hit hit = ValRay.cast(tris, 0.5, 12.0, 0.5, 0.5, 0.0, 0.5);
		assertEquals(10.0, hit.y(), 1e-5);
	}

	@Test
	void ignoresInvisibleStairRamps() {
		List<ValTri> tris = new ArrayList<>();
		tris.add(tri(true, -10, 11, -10, 10, 11, -10, 10, 11, 10));
		tris.add(tri(true, -10, 11, -10, 10, 11, 10, -10, 11, 10));
		tris.addAll(ground(10.0));
		assertEquals(10.0, ValRay.cast(tris, 0.5, 12.0, 0.5, 0.5, 8.0, 0.5).y(), 1e-5);
	}

	@Test
	void placedBlockSitsOnGroundSlightlySunk() {
		// Ground at 10.3: the block goes in cell y=10 (sunk 0.3), not floating in cell 11.
		ValRay.Hit low = ValRay.cast(ground(10.3), 0.5, 12.0, 0.5, 0.5, 8.0, 0.5);
		assertArrayEquals(new int[] { 0, 10, 0 }, ValRay.placementCell(low));
		// Ground at 10.8: the block goes on top (cell 11), floating at most 0.2.
		ValRay.Hit high = ValRay.cast(ground(10.8), 0.5, 12.0, 0.5, 0.5, 8.0, 0.5);
		assertArrayEquals(new int[] { 0, 11, 0 }, ValRay.placementCell(high));
	}

	@Test
	void projectileSticksInTheSurfaceCell() {
		ValRay.Hit hit = ValRay.cast(ground(10.0), 0.5, 12.0, 0.5, 0.5, 8.0, 0.5);
		assertArrayEquals(new int[] { 0, 9, 0 }, ValRay.surfaceCell(hit));
	}

	@Test
	void placementHitStaysWithinServerReachCheck() {
		// The server rejects a use-on-block whose hit point is a block or more from the cell centre.
		for (double y = 10.0; y < 11.0; y += 0.05) {
			ValRay.Hit hit = ValRay.cast(ground(y), 0.5, 13.0, 0.5, 0.5, 7.0, 0.5);
			int[] cell = ValRay.placementCell(hit);
			assertEquals(0.0, Math.max(0.0, Math.abs(hit.y() - (cell[1] + 0.5)) - 1.0), 1e-9);
		}
	}

	@Test
	void dominantFaceMatchesMinecraftDirectionOrder() {
		assertEquals(1, ValRay.dominantFace(0, 1, 0)); // UP
		assertEquals(0, ValRay.dominantFace(0, -1, 0)); // DOWN
		assertEquals(2, ValRay.dominantFace(0, 0.2, -0.9)); // NORTH (-Z)
		assertEquals(3, ValRay.dominantFace(0, 0.2, 0.9)); // SOUTH (+Z)
		assertEquals(4, ValRay.dominantFace(-0.9, 0.2, 0)); // WEST (-X)
		assertEquals(5, ValRay.dominantFace(0.9, 0.2, 0)); // EAST (+X)
	}
}
