# ADR-0001: What this plugin is allowed to delete

Status: Accepted
Date: 2026-09-06
Jira: MGA-106

## Context

MGA is authoritative for the library, so the obvious design is to mirror it: whatever MGA
lists, Playnite shows; whatever MGA drops, Playnite drops. That is wrong in one direction,
and the failure is not recoverable.

A game can leave MGA's answer for reasons that have nothing to do with the user's
intentions:

- the access key names the wrong profile, and that profile has a smaller library;
- a provider connection is mid-reauthentication and returns less than it should;
- the server is reachable but a source failed, and the failure was reported as an empty
  result rather than an error.

The last one is not hypothetical. On 2026-09-06 an MGA Xbox connection answered two
consecutive scans with an empty list and **no error at all**, and all 99 of its games were
marked missing until the next scan brought them back. MGA survived that because its own
deletion is soft and reversible. A Playnite plugin that mirrored the same answer with a
hard delete would have destroyed 99 records, their play time, their completion status,
and — for installed games — the association with files still sitting on the disk.

## Decision

An **installed** game is never deleted, however long MGA has stopped listing it. It is
tagged `MyGamesAnywhere: no longer in your library` and left in place.

An **uninstalled** game that MGA no longer lists is removed. There is nothing of the
user's in such a record: no files, and Playnite keeps no play history for a game that was
never installed through it.

Reconciliation runs **only after a sync that completed**. An empty id set means "MGA
genuinely lists nothing", so a failed or partial listing must never reach it.

A record whose `GameId` is missing is never deleted, because it could not be compared
against MGA's answer in the first place. Deleting it would act on a comparison never made.

## Consequences

- A user whose server is misconfigured sees tags, not loss, and the tags disappear on the
  next good sync.
- A genuinely removed game leaves a tagged record behind if it was installed. That is
  visible, filterable, and removable by hand — the user decides.
- The rule lives in `MgaLibraryReconciliationPlanner`, separate from the code that applies
  it, so it can be tested against plain objects with no Playnite database present. Seven
  tests cover it, each mutated to prove it fails when the rule is broken.
