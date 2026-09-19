package promptprofile

import (
	"fmt"
	"strings"

	"github.com/kataage/lumine/internal/domain"
)

const (
	IllustriousID = "illustrious-xl"
	NoobAIID      = "noobai-xl"
	PonyID        = "pony-xl"
	SDXLID        = "sdxl-generic"
	FluxID        = "flux"
	SD15ID        = "sd15"
	CustomID      = "custom-template"
)

func BuiltIns() []domain.ModelProfile {
	return []domain.ModelProfile{
		{
			ID:             IllustriousID,
			Name:           "Illustrious / ILXL",
			Family:         "illustrious",
			PromptStyle:    "Prefer concise Danbooru-style comma-separated tags. Natural-language fragments are allowed when they express relationships or complex composition better than tags. Never inject Pony score/source tags unless the concrete checkpoint explicitly requires them.",
			QualityTags:    []string{"masterpiece", "best quality", "very aesthetic", "absurdres"},
			TagOrder:       []string{"subject_count", "character", "series", "appearance", "clothing", "pose_action", "expression", "composition", "background", "lighting_effects", "style_artist", "quality"},
			NegativePromptPolicy: "Use concise quality/artifact negatives only when useful (for example worst quality, low quality, lowres, signature, watermark, text, obvious anatomy errors). Never use the negative prompt to remove content the user explicitly requested.",
			LoRATriggerSyntax: "<lora:{name}:{weight}>",
			WeightSyntax:      "({text}:{weight})",
			SystemGuidance: "Preserve canonical Danbooru character/series tags and LoRA trigger words verbatim. Treat checkpoint-specific instructions as higher priority than this generic Illustrious profile.",
			Notes:          "Generic ILXL baseline. Individual Illustrious derivatives can be duplicated into a Custom profile and tuned.",
			BuiltIn:        true,
		},
		{
			ID:             NoobAIID,
			Name:           "NoobAI XL",
			Family:         "noobai",
			PromptStyle:    "Use structured Danbooru/e621-style comma-separated tags. Prefer explicit character, series, artist/style, visual attribute, pose and composition tags over long prose when equivalent.",
			QualityTags:    []string{"masterpiece", "best quality", "newest", "absurdres", "highres"},
			TagOrder:       []string{"subject_count", "character", "series", "artist", "special", "general", "other", "quality"},
			NegativePromptPolicy: "Keep quality/artifact negatives separate from requested subject matter. Prediction type and sampler requirements belong to generation settings, not prompt text.",
			LoRATriggerSyntax: "<lora:{name}:{weight}>",
			WeightSyntax:      "({text}:{weight})",
			SystemGuidance: "Preserve native dataset tags and exact LoRA triggers. Do not silently add safe/nsfw rating tags unless the user's intent or selected checkpoint profile explicitly asks for a rating tag.",
			Notes:          "Generic NoobAI family profile. V-pred/eps runtime settings are intentionally outside the prompt profile.",
			BuiltIn:        true,
		},
		{
			ID:             PonyID,
			Name:           "Pony Diffusion / Pony XL",
			Family:         "pony",
			PromptStyle:    "Use Pony score tags first, then only the relevant source/rating selector, followed by a clear description and useful tags. Do not add Illustrious-style masterpiece/best-quality boilerplate by default.",
			QualityTags:    []string{"score_9", "score_8_up", "score_7_up", "score_6_up", "score_5_up", "score_4_up"},
			TagOrder:       []string{"quality_score", "source", "rating", "subject_count", "character", "appearance", "clothing", "pose_action", "composition", "background", "style"},
			TriggerWords:   []string{"source_anime", "source_pony", "source_furry", "source_cartoon", "rating_safe", "rating_questionable", "rating_explicit"},
			NegativePromptPolicy: "Keep negative prompts minimal unless the concrete Pony derivative documents otherwise; Pony V6 was designed to work without generic quality negatives.",
			LoRATriggerSyntax: "<lora:{name}:{weight}>",
			WeightSyntax:      "({text}:{weight})",
			SystemGuidance: "TriggerWords lists available selectors, not tags to inject all at once. Choose only selectors matching the user's requested source/rating and preserve explicit user choices.",
			Notes:          "Pony-family baseline based on Pony Diffusion V6 XL prompting conventions.",
			BuiltIn:        true,
		},
		{
			ID:             SDXLID,
			Name:           "SDXL Generic",
			Family:         "sdxl",
			PromptStyle:    "Use a clear hybrid of natural language and concise visual tags. Put the main subject and composition before secondary style/detail modifiers. Avoid assuming family-specific score tags.",
			TagOrder:       []string{"subject", "appearance", "action", "composition", "environment", "lighting", "style", "quality"},
			NegativePromptPolicy: "Use negative prompts only for unwanted visual properties or artifacts relevant to the selected checkpoint; avoid giant universal negative lists.",
			LoRATriggerSyntax: "<lora:{name}:{weight}>",
			WeightSyntax:      "({text}:{weight})",
			SystemGuidance: "This is a neutral SDXL fallback. Concrete checkpoint guidance overrides generic conventions.",
			Notes:          "Neutral SDXL template for checkpoints that do not belong to a more specific family.",
			BuiltIn:        true,
		},
		{
			ID:             FluxID,
			Name:           "FLUX",
			Family:         "flux",
			PromptStyle:    "Prefer clear natural-language image descriptions. State the subject, relationships/actions, framing, environment, lighting, material/style and mood explicitly. Use quoted text when exact visible lettering is required.",
			TagOrder:       []string{"subject", "relationships_actions", "composition_camera", "environment", "lighting", "materials_style", "mood", "visible_text"},
			NegativePromptPolicy: "Do not fabricate a large tag-style negative prompt. Keep negative empty unless the concrete FLUX workflow/model explicitly supports and benefits from one.",
			LoRATriggerSyntax: "<lora:{name}:{weight}>",
			WeightSyntax:      "{text}",
			SystemGuidance: "Prefer semantic natural language over Danbooru tag soup. Preserve exact visible text in quotation marks. Keep instructions concrete and visually observable.",
			Notes:          "Generic FLUX-family profile; individual FLUX.1/FLUX.2 or community checkpoints may override workflow-specific behavior.",
			BuiltIn:        true,
		},
		{
			ID:             SD15ID,
			Name:           "Stable Diffusion 1.5 Generic",
			Family:         "sd15",
			PromptStyle:    "Use concise comma-separated visual concepts/tags. Put subject and defining attributes first, then pose/composition/background/style. Natural-language fragments are acceptable when supported by the checkpoint.",
			TagOrder:       []string{"subject", "appearance", "clothing", "pose_action", "composition", "background", "lighting", "style", "quality"},
			NegativePromptPolicy: "Use a focused negative prompt for concrete unwanted artifacts/properties; checkpoint-specific embeddings or negative conventions should be stored in a Custom profile.",
			LoRATriggerSyntax: "<lora:{name}:{weight}>",
			WeightSyntax:      "({text}:{weight})",
			SystemGuidance: "Do not assume SDXL/Pony/Illustrious special tags. Preserve checkpoint and LoRA trigger syntax exactly.",
			Notes:          "Generic SD1.5 compatibility profile.",
			BuiltIn:        true,
		},
		{
			ID:             CustomID,
			Name:           "Custom / Blank Template",
			Family:         "custom",
			PromptStyle:    "Follow the custom checkpoint instructions supplied by the user.",
			TagOrder:       []string{"subject", "composition", "style"},
			NegativePromptPolicy: "Follow the custom checkpoint's documented negative-prompt behavior.",
			LoRATriggerSyntax: "<lora:{name}:{weight}>",
			WeightSyntax:      "({text}:{weight})",
			SystemGuidance: "This template intentionally contains minimal assumptions. Duplicate it before editing.",
			Notes:          "Blank starting point for a user-defined checkpoint/profile.",
			BuiltIn:        true,
		},
	}
}

func BuiltIn(id string) (domain.ModelProfile, bool) {
	for _, profile := range BuiltIns() {
		if profile.ID == id {
			return profile, true
		}
	}
	return domain.ModelProfile{}, false
}

func Validate(profile domain.ModelProfile) error {
	if strings.TrimSpace(profile.ID) == "" {
		return fmt.Errorf("model profile id is required")
	}
	if strings.TrimSpace(profile.Name) == "" {
		return fmt.Errorf("model profile name is required")
	}
	if strings.TrimSpace(profile.Family) == "" {
		return fmt.Errorf("model profile family is required")
	}
	if len(profile.QualityTags) > 128 || len(profile.TagOrder) > 128 || len(profile.TriggerWords) > 256 {
		return fmt.Errorf("model profile contains too many list entries")
	}
	for _, value := range append(append(append([]string{}, profile.QualityTags...), profile.TagOrder...), profile.TriggerWords...) {
		if len([]rune(value)) > 512 {
			return fmt.Errorf("model profile list entry is too long")
		}
	}
	for _, value := range []string{
		profile.Name, profile.Family, profile.CheckpointName, profile.PromptStyle,
		profile.NegativePromptPolicy, profile.LoRATriggerSyntax, profile.WeightSyntax,
		profile.SystemGuidance, profile.Notes,
	} {
		if len([]rune(value)) > 12000 {
			return fmt.Errorf("model profile field is too long")
		}
	}
	return nil
}
