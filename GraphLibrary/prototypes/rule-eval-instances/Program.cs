// PROTOTYPE — throwaway TUI shell. Drives BOTH models in lockstep. See README.md.
using RuleEvalPrototype;

var models = new IConfigModel[] { new CoreNativeModel(), new OverlayModel() };
var layers = Scenario.Layers;
int layerIx = 0;
int nextId = 1;

if (args.Contains("--selftest")) { SelfTest.Run(); return; }

const string BOLD = "\x1b[1m", DIM = "\x1b[2m", GRN = "\x1b[32m", RED = "\x1b[31m",
             YEL = "\x1b[33m", RST = "\x1b[0m";

void Add(string proto)
{
    var id = new InstanceId(nextId++);
    foreach (var m in models) m.AddInstance(id, proto, layers[layerIx]);
}
void Undo() { if (nextId > 1) { nextId--; foreach (var m in models) m.RemoveLast(); } }

while (true)
{
    Console.Write("\x1b[2J\x1b[H");
    var layer = layers[layerIx];

    Console.WriteLine($"{BOLD}SAME CONFIGURATION — TWO REPRESENTATIONS{RST}");
    Console.WriteLine($"{DIM}rule-eval instances/layers prototype · ticket #3{RST}\n");

    Console.WriteLine($"{BOLD}Rules in play (declared once over the base graph):{RST}");
    foreach (var r in Scenario.Rules) Console.WriteLine($"  {DIM}{r.Describe()}{RST}");
    Console.WriteLine();

    Console.WriteLine($"Current layer: {BOLD}{layer}{RST}   {DIM}(all layers: {string.Join(", ", layers)}){RST}\n");

    foreach (var m in models)
    {
        Console.WriteLine($"{BOLD}--- {m.Name} ---{RST}");
        Console.Write(m.DumpStructure());
        Console.WriteLine();
    }

    // Both models feed the SAME rule engine. Show that answers match.
    var views = models.Select(m => m.Project()).ToArray();
    var reports = views.Select(Engine_.Validate).ToArray();
    bool match = reports[0].Violations.Count == reports[1].Violations.Count;

    var flag = match ? $"{GRN}match ✓{RST}" : $"{RED}DIVERGE ✗{RST}";
    Console.WriteLine($"{BOLD}Validation across all layers{RST}  " +
                      $"[(i): {StatusOf(reports[0])}  (ii): {StatusOf(reports[1])}]  {flag}");
    if (reports[0].Ok) Console.WriteLine($"  {GRN}valid configuration{RST}");
    foreach (var v in reports[0].Violations)
    {
        var col = v.Kind == ViolationKind.Requirement ? YEL : RED;
        Console.WriteLine($"  {col}• [{v.Kind}] {v.Message}{RST}");
    }
    Console.WriteLine();

    Console.WriteLine($"{BOLD}Live availability — adding into '{layer}'{RST} {DIM}(both models agree){RST}");
    var avail = Engine_.AvailabilityIn(views[0], layer, nextId);
    foreach (var a in avail)
    {
        var tag = a.CanAdd ? $"{GRN}available{RST}" : $"{RED}blocked{RST}  ";
        Console.WriteLine($"  {tag}  {a.Prototype,-9} {DIM}{a.Note}{RST}");
    }
    Console.WriteLine();

    Console.WriteLine($"{BOLD}Add into '{layer}':{RST}");
    for (int i = 0; i < Scenario.Prototypes.Length; i++)
        Console.Write($"  {BOLD}[{i + 1}]{RST} {DIM}{Scenario.Prototypes[i]}{RST}");
    Console.WriteLine();
    Console.WriteLine($"  {BOLD}[l]{RST} {DIM}cycle layer{RST}   {BOLD}[x]{RST} {DIM}undo last{RST}   {BOLD}[q]{RST} {DIM}quit{RST}");

    var key = Console.ReadKey(true).KeyChar;
    if (key == 'q') break;
    if (key == 'l') { layerIx = (layerIx + 1) % layers.Length; continue; }
    if (key == 'x') { Undo(); continue; }
    if (char.IsDigit(key))
    {
        int n = key - '1';
        if (n >= 0 && n < Scenario.Prototypes.Length) Add(Scenario.Prototypes[n]);
    }
}

static string StatusOf(ValidationReport r) => r.Ok ? "OK" : $"{r.Violations.Count} issue(s)";
