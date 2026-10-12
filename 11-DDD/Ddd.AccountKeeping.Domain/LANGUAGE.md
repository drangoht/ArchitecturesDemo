# Ubiquitous language — "Account keeping" context

The vocabulary shared by the business experts and the developers. Each term exists as is in the code:
if an expert says "daily withdrawal limit", you find `DailyWithdrawalLimit`, not `MaxPerDayCap`.

| Term | Definition | In the code |
|---|---|---|
| **Account** | Deposit account kept by the bank, identified by an IBAN, denominated in a single currency. | `Account` (aggregate) |
| **Owner** | Person in whose name the account is opened. | `OwnerName` (value object) |
| **IBAN** | International identifier of the account, with check digits (modulo 97). | `Iban` (value object) |
| **Money** | An amount **and** its currency. 10 € and 10 $ are not the same thing; they are not added together. | `Money` (value object) |
| **Currency** | Currency the account is kept in: EUR, USD, CHF. | `Currency` (value object) |
| **Balance** | Money available on the account. Never negative (no overdraft). | `Account.Balance` |
| **Movement** | Any entry on the account: deposit, withdrawal, transfer sent or received. | `Movement` (entity of the aggregate) |
| **Withdrawal** | Money taken out at the counter or at the cash machine. Subject to the daily limit. | `Account.Withdraw` |
| **Daily withdrawal limit** | Total of withdrawals allowed on the same day. Transfers are not subject to it. | `Account.DailyWithdrawalLimit` |
| **Transfer** | Transfer from one account of the bank to another, designated by its IBAN. Expressed in the currency of the debited account; converted if the credited account is in another currency. | `TransferService` (domain service) |
| **Block** | A blocked account can no longer be debited. It can still receive transfers. | `Account.Block` |

The same word can have another meaning in another context: for **Compliance**, an account is a
**vigilance file**, which knows neither balance nor limit (see `Ddd.Compliance/LANGUAGE.md`).
