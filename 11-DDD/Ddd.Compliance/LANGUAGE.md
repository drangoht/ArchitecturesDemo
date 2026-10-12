# Ubiquitous language — "Compliance" context

For the Compliance team, an account has neither a balance nor a limit: it is a **source of flows to monitor**.
Same reality, another model, other words. That is why it is a separate bounded context.

| Term | Definition | In the code |
|---|---|---|
| **Vigilance file** | What Compliance knows about an account: the observed transfers and the raised alerts. | `VigilanceFile` (aggregate) |
| **Observed transfer** | A transfer as seen by Compliance: a date, an amount **in euros**, a beneficiary. The original currency does not matter to it. | `ObservedTransfer` (value object) |
| **Reporting threshold** | Amount (10,000 €) from which a transfer must be reported. | `VigilanceFile.ReportingThreshold` |
| **Structuring** | Splitting a large sum into several small, close transfers to fly under the radar. Suspected from 3 transfers in 10 minutes. | `VigilanceFile.StructuringWindow` |
| **Alert** | Report of a behaviour to examine: *high* (to analyse) or *critical* (preventive block requested). | `Alert` |
| **Preventive block** | Request made to Account keeping to block an account pending the analysis. | via `IAccountKeepingApi` |
