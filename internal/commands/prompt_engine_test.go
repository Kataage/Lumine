package commands

import (
	"strings"
	"testing"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/ai/llamacpp"
)

func TestValidatePromptEngineRequest(t *testing.T) {
	valid := []PromptEngineRequestDTO{
		{Operation: llamacpp.PromptOperationIdea, Idea: "idea"},
		{Operation: llamacpp.PromptOperationImprove, Positive: "1girl"},
		{Operation: llamacpp.PromptOperationConvert, Positive: "1girl", TargetProfile: "FLUX"},
		{Operation: llamacpp.PromptOperationEdit, Positive: "1girl", Instruction: "背景だけ変更"},
	}
	for _, request := range valid {
		if err := validatePromptEngineRequest(request); err != nil {
			t.Fatalf("%s should be valid: %v", request.Operation, err)
		}
	}
	if err := validatePromptEngineRequest(PromptEngineRequestDTO{Operation: "chat", Idea: "hello"}); err == nil {
		t.Fatal("general chat must not be accepted")
	}
	if err := validatePromptEngineRequest(PromptEngineRequestDTO{
		Operation: llamacpp.PromptOperationIdea,
		Idea:      strings.Repeat("x", 12001),
	}); err == nil {
		t.Fatal("oversized request must fail")
	}
}

func TestPromptEngineResultFromResponse(t *testing.T) {
	result, err := promptEngineResultFromResponse(ai.InferenceResponse{Payload: map[string]any{
		"positive":         " 1girl, white hair ",
		"negative":         " lowres ",
		"characters":       []string{" heroine "},
		"loras":            []string{" <lora:test:0.8> "},
		"composition":      " upper body ",
		"notes":            []string{" note "},
		"completionTokens": 51,
	}})
	if err != nil {
		t.Fatalf("promptEngineResultFromResponse: %v", err)
	}
	if result.Positive != "1girl, white hair" || result.CompletionTokens != 51 {
		t.Fatalf("result = %+v", result)
	}
}
