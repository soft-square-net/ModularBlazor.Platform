---
title: Storage
description: "Level-1 dataflow of Module.Storage: folders and files, chunked uploads, versions, ACLs, groups, sharing and public links, the recycle bin, and the processor and sweeper over S3 or the file system."
sidebar:
  order: 17
---

`storage` (Module.Storage, v0.1.0, **core pool**) is the tenant's file system. Blobs live in an S3-compatible
store or on the file system (`BlobStores`: `IBlobStore` per location). Two client modules use it:

- `Module.Storage`, the Files app, at `/files`
- `Module.Storage.Shares`, the anonymous share pages, at `/s`, inside `Layouts.Public`

```mermaid
flowchart LR
  User(["Signed-in user<br/>(Files)"]):::ext
  Anon(["Anyone with a link"]):::ext

  subgraph Storage["storage (/api/storage)"]
    direction TB
    F1p("17.1 Browse"):::custom
    F2p("17.2 Uploads"):::custom
    F3p("17.3 Items + versions"):::custom
    F4p("17.4 Access + groups"):::custom
    F5p("17.5 Sharing"):::custom
    F6p("17.6 Public shares"):::custom
    F7p("17.7 Recycle bin"):::custom
    F8p("17.8 Processor + sweeper"):::custom
  end

  Db[("schema storage")]:::store
  Blob[("Object storage<br/>S3 or file system")]:::store

  User <-->|F1| F1p
  User <-->|F2| F2p
  User <-->|F3| F3p
  User <-->|F4| F4p
  User <-->|F5| F5p
  User <-->|F7| F7p
  Anon <-->|F6| F6p
  F1p & F2p & F3p & F4p & F5p & F6p & F7p & F8p <--> Db
  F2p & F3p & F6p & F8p <-->|F8| Blob

  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
```

| Flow | Route | Payload |
|---|---|---|
| F1 | `GET roots`, `folders/{id}/items`, `items/{id}`, `usage`, `events` | → `Roots`, `FolderView`, `ItemDetail`, `StorageUsage`, `EventPage` |
| F2 | `POST uploads`, `PUT uploads/{id}/chunks`, `POST uploads/{id}/complete`, `GET/DELETE uploads/{id}` | `StartUploadCommand`, chunk (`FileParameter`) → `UploadDto`, `ItemDto` |
| F3 | `POST folders`, `PATCH items/{id}`, `items/move`, `copy`, `delete`, `versions/{n}/restore`, `DELETE versions/{n}`, `items/{id}/content`, `items/{id}/link` | `CreateFolderCommand`, `RenameRequest`, `MoveItemsCommand`, `CopyItemsCommand`, `DeleteItemsCommand` → `ItemDto`, `BatchResult`, `ItemDetail`, `FileResponse`, `DownloadLink` |
| F4 | `GET/PUT items/{id}/acl`, `PUT owner`, `GET effective-access`, `groups` (CRUD), `people` | `SetAclRequest`, `SetOwnerRequest`, `GroupInput` → `AclDto`, `EffectiveAccessDto`, `GroupDto`, `PersonDto` |
| F5 | `items/{id}/sharing`, `sharing/people`, `sharing/public`, `sharing/links`, `DELETE sharing/links/{id}`, `shared-with-me` | `ShareWithPeopleRequest`, `SetPublicRequest`, `CreateShareLinkRequest` → `SharingDto`, `ShareLinkDto`, `SharedItemDto` |
| F6 | `GET links/{token}`, `shares/{tenant}/{token}`, `shares/{tenant}/{token}/files/{id}/link`, `public/{tenant}/{fileId}/{name}` | → `SharedView`, `DownloadLink`, a file stream |
| F7 | `GET recycle-bin`, `POST recycle-bin/restore`, `purge` | `RestoreItemsCommand`, `PurgeItemsCommand` → `DeletedItemDto[]`, `BatchResult` |
| F8 | `IBlobStore` (`AWSSDK.S3`, or `FileSystemBlobStore`); `StorageProcessor` scans files and makes thumbnails; `StorageSweeper` and `POST maintenance/sweep` clean up | blobs, chunks → `SweepResult` |

**Tables (schema `storage`):** `items`, `file_versions`, `blobs`, `uploads`, `access_entries`, `groups`,
`group_members`, `people`, `share_links`, `events`.

The module publishes and consumes no integration events. The string `soft.square.net.storage.download-links.v1` in
the code is a Data Protection purpose for signed download links, not an event.
