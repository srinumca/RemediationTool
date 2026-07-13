import ast
from pathlib import Path

SCRIPT_PATH = Path("tools/apply_phase2_ingestion.py")
source = SCRIPT_PATH.read_text(encoding="utf-8")
module = ast.parse(source)

seen: dict[str, int] = {}
new_body: list[ast.stmt] = []


def get_old_text(call: ast.Call) -> str | None:
    if len(call.args) < 2:
        return None
    value = call.args[1]
    return value.value if isinstance(value, ast.Constant) and isinstance(value.value, str) else None


def convert_to_count(call: ast.Call, expected: int) -> None:
    if isinstance(call.func, ast.Name):
        call.func.id = "replace_count"
    if len(call.args) == 3:
        call.args.append(ast.Constant(expected))
    else:
        call.args[-1] = ast.Constant(expected)


for statement in module.body:
    if not isinstance(statement, ast.Expr) or not isinstance(statement.value, ast.Call):
        new_body.append(statement)
        continue

    call = statement.value
    old = get_old_text(call)
    if old is None:
        new_body.append(statement)
        continue

    # Manual ProcessAsync and Step Function IngestAsync contain identical blocks.
    duplicate_groups = {
        "prepare": "var stagingWritten = await PrepareResumeStoreAsync(\n",
        "persist": "            PersistValidFindingsInBatches(\n",
        "summary": "var storedMetadataPath = await StoreProcessingSummaryAsync(\n",
        "failure-summary": "TryStoreProcessingSummaryAsync(response, parseResult?.RejectedRows)",
    }

    matched_group = next(
        (name for name, marker in duplicate_groups.items() if marker in old),
        None)
    if matched_group is not None:
        occurrence = seen.get(matched_group, 0)
        seen[matched_group] = occurrence + 1
        if occurrence == 0:
            convert_to_count(call, 2)
            new_body.append(statement)
        # Drop the duplicate Step Function transform.
        continue

    # Four transform calls cover three concrete source statements. One transform
    # is a larger resume block that becomes redundant after the global replacement.
    if "CleanupStagingForCompletedJob(response);" in old:
        occurrence = seen.get("cleanup", 0)
        seen["cleanup"] = occurrence + 1
        if occurrence == 0:
            call.args[1] = ast.Constant(
                "                CleanupStagingForCompletedJob(response);\n")
            call.args[2] = ast.Constant(
                "                await CleanupStagingForCompletedJobAsync(response, cancellationToken);\n")
            convert_to_count(call, 3)
            new_body.append(statement)
        continue

    # Resume has three summary writes: load failure, no remaining records and
    # successful completion. The later larger success block becomes redundant.
    if old == "                response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(response);\n":
        convert_to_count(call, 3)
        new_body.append(statement)
        continue

    if (
        "response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(response);" in old
        and "response.MetadataJsonPath = response.ProcessingSummaryPath;" in old
    ):
        continue

    new_body.append(statement)

module.body = new_body
ast.fix_missing_locations(module)
SCRIPT_PATH.write_text(ast.unparse(module) + "\n", encoding="utf-8")

required_counts = {
    "prepare": 2,
    "persist": 2,
    "summary": 2,
    "failure-summary": 2,
    "cleanup": 4,
}
for group, expected in required_counts.items():
    actual = seen.get(group, 0)
    if actual != expected:
        raise RuntimeError(
            f"Unexpected Phase 2 transform count for {group}: expected {expected}, found {actual}")

print("Phase 2 transformation script normalized successfully.")
