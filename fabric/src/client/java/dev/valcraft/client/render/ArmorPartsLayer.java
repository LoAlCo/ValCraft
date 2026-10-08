package dev.valcraft.client.render;

import com.mojang.blaze3d.vertex.PoseStack;
import com.mojang.math.Axis;
import dev.valcraft.ValCraft;
import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import net.minecraft.client.Minecraft;
import net.minecraft.client.model.HumanoidModel;
import net.minecraft.client.model.geom.ModelPart;
import net.minecraft.client.renderer.SubmitNodeCollector;
import net.minecraft.client.renderer.entity.RenderLayerParent;
import net.minecraft.client.renderer.entity.layers.RenderLayer;
import net.minecraft.client.renderer.entity.state.HumanoidRenderState;
import net.minecraft.client.renderer.item.ItemStackRenderState;
import net.minecraft.client.renderer.texture.OverlayTexture;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;
import net.minecraft.world.item.ItemDisplayContext;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;

/**
 * The 3D parts of ValCraft's Valheim armor, the shapes a flat armor texture can't make: the Drake
 * helmet's horns, the antlers of the Flametal helmet and the Caller's headdress, the bear's ears and
 * snout, the Protector's wings, spiked crests, a helm's point... (tools/armor_parts.py builds them as
 * blocky item models, listed in valcraft/armor_parts.tsv: armor item, part model, where it's worn).
 *
 * <p>Each part is drawn on the wearer's head, body or an arm the way Minecraft draws a pumpkin on a
 * head: the part model's 16-unit cube is the armor around that body part (a helmet's surface is its
 * 0..16 box), its centre the body part's centre. Players, armor stands, zombies and skeletons all wear
 * them. Valheim gets them too (AvatarExporter captures what the player renderer submits).
 */
public final class ArmorPartsLayer<S extends HumanoidRenderState, M extends HumanoidModel<S>> extends RenderLayer<S, M> {
	private enum Where { HEAD, BODY, RIGHT_ARM, LEFT_ARM }

	private record Part(Identifier model, Where where) {
	}

	private static final Map<String, List<Part>> PARTS = new HashMap<>();
	private static boolean loaded, warned;

	public ArmorPartsLayer(RenderLayerParent<S, M> parent) {
		super(parent);
	}

	private static void load() {
		if (loaded) {
			return;
		}
		loaded = true;
		var in = ArmorPartsLayer.class.getResourceAsStream("/valcraft/armor_parts.tsv");
		if (in == null) {
			return;
		}
		try (var reader = new BufferedReader(new InputStreamReader(in, StandardCharsets.UTF_8))) {
			for (String line; (line = reader.readLine()) != null; ) {
				if (line.isBlank() || line.startsWith("#")) {
					continue;
				}
				String[] c = line.split("\t");
				PARTS.computeIfAbsent(c[0], k -> new ArrayList<>()).add(new Part(Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, c[1]), Where.valueOf(c[2])));
			}
		} catch (Exception e) {
			ValCraft.LOG.warn("ValCraft: couldn't read armor_parts.tsv", e);
		}
	}

	@Override
	public void submit(PoseStack pose, SubmitNodeCollector collector, int light, S state, float yRot, float xRot) {
		load();
		if (PARTS.isEmpty() || state.isInvisible) {
			return;
		}
		try {
			draw(pose, collector, light, state.headEquipment);
			draw(pose, collector, light, state.chestEquipment);
			draw(pose, collector, light, state.legsEquipment);
		} catch (RuntimeException e) {
			if (!warned) {
				warned = true;
				ValCraft.LOG.warn("ValCraft: couldn't draw the 3D parts of Valheim armor", e);
			}
		}
	}

	private void draw(PoseStack pose, SubmitNodeCollector collector, int light, ItemStack worn) {
		if (worn == null || worn.isEmpty()) {
			return;
		}
		var key = BuiltInRegistries.ITEM.getKey(worn.getItem());
		if (!key.getNamespace().equals(ValCraft.MOD_ID)) {
			return;
		}
		List<Part> parts = PARTS.get(key.getPath());
		if (parts == null) {
			return;
		}
		M model = this.getParentModel();
		for (Part part : parts) {
			ItemStack stack = new ItemStack(Items.STICK);
			stack.set(DataComponents.ITEM_MODEL, part.model());
			ItemStackRenderState item = new ItemStackRenderState();
			Minecraft.getInstance().getItemModelResolver().updateForTopItem(item, stack, ItemDisplayContext.HEAD, Minecraft.getInstance().level, null, 0);
			if (item.isEmpty()) {
				continue;
			}
			pose.pushPose();
			model.root().translateAndRotate(pose);
			ModelPart on = switch (part.where()) {
				case HEAD -> model.head;
				case BODY -> model.body;
				case RIGHT_ARM -> model.rightArm;
				case LEFT_ARM -> model.leftArm;
			};
			on.translateAndRotate(pose);
			// the centre of that body part (model y points down from its pivot)
			switch (part.where()) {
				case HEAD -> pose.translate(0.0F, -0.25F, 0.0F);
				case BODY -> pose.translate(0.0F, 0.375F, 0.0F);
				case RIGHT_ARM -> pose.translate(-0.0625F, 0.25F, 0.0F);
				case LEFT_ARM -> pose.translate(0.0625F, 0.25F, 0.0F);
			}
			pose.rotateDegrees(Axis.YP, 180.0F);
			pose.scale(0.625F, -0.625F, -0.625F);
			item.submit(pose, collector, light, OverlayTexture.NO_OVERLAY, 0);
			pose.popPose();
		}
	}
}
