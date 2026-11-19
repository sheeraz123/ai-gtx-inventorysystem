# Billing Phase 1: captured scope and unanswered questions

This document records the user's requested scope. No Billing implementation is included in Harness Milestone 1. The requirements are incomplete and are not a tax or legal specification.

## Requested scope

- Multi-company configuration and Company Master: IEC, GSTIN, multiple contacts and up to two optional logos.
- Customer Master: billing and multiple shipping addresses.
- Product, Category, Unit and HSN/SAC Masters.
- Configurable GST calculations and effective-date tax rules.
- Proforma Invoices, conversion to Sales Invoices and direct Sales Invoices.
- Configurable invoice numbering, A4 PDF generation/preview and company-specific templates.
- Configurable email and SMS notifications.
- Role-based permissions with one Super Admin, audit logging and backup configuration.
- Database abstraction that considers the existing Microsoft Access workflow and future migration.

Inventory transactions, manufacturing and full accounting are outside Phase 1. Future ERP modules must not be introduced implicitly through Billing.

## Questions requiring stakeholder decisions

1. What industries and invoice examples are authoritative? Which invoice/document types, fields, languages and template constraints are required?
2. How are companies isolated, how are users granted company access, and is the single Super Admin global across companies? What is the recovery and transfer process?
3. What existing Access files, schema, relationships, queries, reports, data quality issues and workflows must be preserved? Is Access initially the system of record or an import source? What connectivity, deployment and migration constraints exist?
4. Which GST scenarios, jurisdictions, effective dates, supply classifications, exemptions, reverse-charge cases, discounts and rounding rules apply? Who supplies and approves the authoritative tax rules? No rates or calculations are assumed here.
5. How are IEC/GSTIN and contact/address fields validated, and which are optional? What logo formats, dimensions and retention rules apply?
6. Are product price/tax defaults company-specific? How do units, categories and HSN/SAC classifications change over time?
7. What are the proforma approval, editing, expiration and conversion rules? Can conversion be partial, repeated or consolidated? What becomes immutable after issue?
8. How do numbering series vary by company, document type, period and branch? What are concurrency, gap, cancellation and reset rules?
9. Who approves templates and required statutory content? Which printers and PDF/preview environments must work?
10. What email/SMS providers, recipients, consent, templates, delivery retries and notification events are required?
11. What are the audit retention, timezone, backup schedule, encryption, storage, recovery objectives and restore-verification requirements?
12. What application UI, hosting model, operating systems, authentication mechanism, concurrency and offline needs must be supported? How will existing data be reconciled during cutover?

Resolve these questions through examples and acceptance tests before implementing Billing. Keep sensitive customer/company data and credentials outside the repository.
