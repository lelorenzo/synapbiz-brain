# Flow workbench fixtures

Golden texts and synthetic documents of the flow surface (`Synapbiz.Brain.Workbench.Flows`).

- `<id>.view.en.txt` and `<id>.view.es.txt`: the step-by-step description of each reference document in both languages (`FlowDescriber`), pinned by `FlowDescriberTests`.
- `*.process.json`: synthetic `brain.process@1` documents that exercise what the six reference documents do not (a dead route, a loop over a list, a timer tied to an instant, and so on). Every one is invented; none holds personal data.

To rewrite the golden texts after a deliberate change, run the tests once with the variable `BRAIN_UPDATE_GOLDEN=1` and read the diff.
