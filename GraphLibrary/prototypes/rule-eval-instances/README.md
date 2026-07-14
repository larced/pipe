# PROTOTYPE — throwaway. Wayfinder ticket larced/pipe#3

## The question

When a node has multiplicity, each **instance** is a distinct addressable thing that can be
tagged into a **layer / subgraph** while its prototype node still lives in the graph at large.
Where should "instance" and "layer membership" live?

- **Model (i) Core-native:** instances are real graph nodes; layer membership and instance-of
  are edges. The base graph *grows* as you select.
- **Model (ii) Selection overlay:** the base graph stays lean (handles + payload, per #5);
  instances + layer tags live in a separate selection/configuration state above the graph.

This prototype drives **one configuration through both models in lockstep** so you can feel:
how each stores the same picture, and whether rule-validation "across layers" and live
per-node availability come out the same under each.

## Run

    dotnet run --project GraphLibrary/prototypes/rule-eval-instances

Then press keys to add instances into the current layer and watch both representations +
the shared validation / availability update.
