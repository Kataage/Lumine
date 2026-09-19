import type { AdvancedVisionResult } from "../api/client";

function Field({ label, value }: { label: string; value?: string }) {
  if (!value) return null;
  return (
    <div>
      <p className="text-[9px] font-medium text-muted-foreground">{label}</p>
      <p className="mt-0.5 whitespace-pre-wrap break-words text-[10px] leading-relaxed text-foreground">{value}</p>
    </div>
  );
}

function ListField({ label, values }: { label: string; values?: string[] }) {
  if (!values?.length) return null;
  return <Field label={label} value={values.join(" / ")} />;
}

export function AdvancedVisionResultView({ result }: { result: AdvancedVisionResult }) {
  return (
    <div className="space-y-2">
      <Field label="概要" value={result.summary} />
      <ListField label="被写体" values={result.subjects} />
      <Field label="環境" value={result.environment} />
      <Field label="構図" value={result.composition} />
      <Field label="視点" value={result.viewpoint} />
      <ListField label="動作" values={result.actions} />
      <ListField label="関係" values={result.relationships} />
      <Field label="文脈" value={result.context} />
      <ListField label="差分" values={result.differences} />
      <ListField label="共通点" values={result.commonalities} />
      <ListField label="Reverse Promptヒント" values={result.reversePromptHints} />
      <ListField label="画像内テキスト" values={result.visibleText} />
      <ListField label="補足" values={result.notes} />
      {result.completionTokens ? (
        <p className="text-[9px] text-muted-foreground">completion tokens: {result.completionTokens}</p>
      ) : null}
    </div>
  );
}
