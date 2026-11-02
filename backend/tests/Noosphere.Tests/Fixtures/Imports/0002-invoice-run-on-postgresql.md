---
status: accepted
date: 2026-04-15
deciders: Team Finance
---
# Run the invoice batch on PostgreSQL

## Context and Problem Statement

The monthly invoice run of Billing takes six hours on the current database.

## Considered Options

* PostgreSQL partitioning
* A separate reporting database

## Decision Outcome

Chosen option: "PostgreSQL partitioning", because Billing already runs on PostgreSQL.
