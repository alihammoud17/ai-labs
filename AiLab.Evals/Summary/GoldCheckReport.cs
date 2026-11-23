using System.Text;
using AiLab.Evals.AskSet;
using AiLab.Evals.Evaluators;

namespace AiLab.Evals.Summary;

// GoldCheck over stored results, no model call: the same function the live "Gold check" metric uses, fed from the
// stored answer, status, gate and citations (EvalSummary.Parse). One row per execution, run 1 and all runs, then every
// run's verdict and each Wrong run's reason. Missing results show "no data", never 0 correct.
public static class GoldCheckReport
{
    public const int ExpectedRunsPerCell = 90;   // 30 rows × 3 runs
    public const int CheckedRowsPerRun = 16;      // 5 exact + 6 all items + 4 abstain + 1 gate

    public sealed record Cell(string Execution, IReadOnlyList<RunRecord>? Runs);

    public sealed record Verdict(RunRecord Run, AskSetRow Row, GoldCheckResult Result);

    public sealed record Counts(int Runs, int Checked, int Correct, int Wrong, int NotChecked);

    public static Verdict Check(RunRecord run)
    {
        var row = AskSetData.Row(run.Id);
        return new Verdict(run, row,
            GoldCheck.Evaluate(row, run.Answer, run.Status, run.Outcome.GetValueOrDefault("gate", ""), run.Citations));
    }

    public static Counts Count(IEnumerable<Verdict> verdicts)
    {
        var list = verdicts.ToList();
        int correct = list.Count(v => v.Result.Verdict == GoldVerdict.Correct);
        int wrong = list.Count(v => v.Result.Verdict == GoldVerdict.Wrong);
        return new Counts(list.Count, correct + wrong, correct, wrong, list.Count - correct - wrong);
    }

    public static string Markdown(IReadOnlyList<Cell> cells)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Gold check over stored results");
        sb.AppendLine();
        sb.AppendLine("`GoldCheck` (Task 2.2) on each stored run: facts from the row's `match` list, then a citation that holds a gold line; " +
                      "abstain rows by status or phrase; E5 by the gate. Judge and partial rows are not checked. No model call. " +
                      $"A complete cell has {ExpectedRunsPerCell} runs, {CheckedRowsPerRun * 3} of them checked ({CheckedRowsPerRun} per run).");
        sb.AppendLine();
        sb.AppendLine("| Cell | Runs | Checked | Correct | Wrong | Not checked | Run 1: correct / checked | Answer rows run 1 /11 | Abstain all runs /12 | E5 gated /3 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");

        var checkedCells = new List<(string Execution, List<Verdict> Verdicts)>();
        var notes = new List<string>();
        foreach (var cell in cells)
        {
            if (cell.Runs is null || cell.Runs.Count == 0)
            {
                sb.AppendLine($"| {cell.Execution} | no data | | | | | | | | |");
                notes.Add($"- **{cell.Execution}: no stored results.** Nothing was checked; this is not 0 correct.");
                continue;
            }

            var verdicts = cell.Runs.Select(Check).ToList();
            checkedCells.Add((cell.Execution, verdicts));
            var all = Count(verdicts);
            var run1 = Count(verdicts.Where(v => v.Run.Iteration == "1"));
            var answers1 = Count(verdicts.Where(v => v.Run.Iteration == "1" && v.Row.Check is "exact" or "all items"));
            var abstains = Count(verdicts.Where(v => v.Row.Check == "abstain" && v.Row.Expected != "gate"));
            var gate = Count(verdicts.Where(v => v.Row.Expected == "gate"));

            sb.AppendLine($"| {cell.Execution} | {all.Runs} | {all.Checked} | {all.Correct} | {all.Wrong} | {all.NotChecked} | " +
                          $"{run1.Correct} / {run1.Checked} | {answers1.Correct} / {answers1.Checked} | {abstains.Correct} / {abstains.Checked} | {gate.Correct} / {gate.Checked} |");

            if (all.Runs != ExpectedRunsPerCell || all.Checked != CheckedRowsPerRun * 3)
                notes.Add($"- **{cell.Execution}: incomplete** ({all.Runs} runs, {all.Checked} checked; expected {ExpectedRunsPerCell} and {CheckedRowsPerRun * 3}). Missing runs count as failed under the rule (Task 1.4).");
        }

        if (notes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Check before using");
            sb.AppendLine();
            foreach (string note in notes)
                sb.AppendLine(note);
        }

        foreach (var (execution, verdicts) in checkedCells)
        {
            sb.AppendLine();
            sb.AppendLine($"## {execution}");
            sb.AppendLine();
            sb.AppendLine("| ID | Check | Run 1 | Run 2 | Run 3 |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var row in verdicts.Where(v => v.Result.Verdict != GoldVerdict.NotChecked)
                         .GroupBy(v => v.Row.Id).OrderBy(g => g.First().Run.Order))
            {
                string Mark(string iteration) => row.FirstOrDefault(v => v.Run.Iteration == iteration)?.Result.Verdict switch
                {
                    GoldVerdict.Correct => "correct",
                    GoldVerdict.Wrong => "**wrong**",
                    _ => "no data",
                };
                var first = row.First().Row;
                string check = first.Expected == "gate" ? "gate" : first.Check;
                sb.AppendLine($"| {row.Key} | {check} | {Mark("1")} | {Mark("2")} | {Mark("3")} |");
            }

            var wrong = verdicts.Where(v => v.Result.Verdict == GoldVerdict.Wrong).ToList();
            if (wrong.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Wrong runs:");
                sb.AppendLine();
                foreach (var v in wrong)
                    sb.AppendLine($"- {v.Row.Id} run {v.Run.Iteration}: {Escape(v.Result.Reason)}");
            }
        }

        return sb.ToString();
    }

    // Keep a reason on one markdown line
    private static string Escape(string text) => text.Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|");
}
