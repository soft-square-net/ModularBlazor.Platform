---
title: Tutorials
description: Step-by-step walkthroughs of the newsroom. Set up an organization and its people, follow one post from proposal to publication, and add a field of your own with its editor.
sidebar:
  order: 0
---

These tutorials walk through the newsroom modules together, in the order a real tenant meets them. Each step names the
page or the API route it uses, and links to the module page that has the full rules.

| Tutorial | You will |
| --- | --- |
| [1. Set up a newsroom](/tutorials/newsroom-setup/) | Create the organization, its units and Sections, the press and its categories, the people, their positions and standing rights, the shift roles, and the service accounts |
| [2. The life of a post](/tutorials/post-lifecycle/) | Follow one creative work (a news story, an article, a video...) from a proposal to the published page, through moderation, the workflow, the shift, the contents' review, the studio and the Desk |
| [3. A new field and its editor](/tutorials/custom-field/) | Define a field in DynamicFields, set its values, limits and style, connect it to an editor dialog, and see it in the Content Designer |

## Before you start

- **Modules.** The full newsroom uses [Organization](/modules/organization/), [Shifts](/modules/shifts/),
  [Contents](/modules/contents/), [DynamicFields](/modules/dynamic-fields/), [NewsRoom](/modules/newsroom/),
  [Elsa](/modules/elsa/) and the [ContentDesigner](/modules/content-designer/). Each is installed the same way: the
  server part on **Host Modules** (`/host-modules`), then its client parts on **Client Modules** (`/client-modules`),
  the API client first. See [runtime modules](/backend/runtime-modules/).
- **A place to try it.** Every module repository has an Aspire playground. Elsa's playground runs all of them together
  (Organization, Shifts, DynamicFields and Content in a `core` pool, Elsa in a `workflow` pool, NewsRoom optional),
  which is the closest to these tutorials. The playgrounds use the platform lab's ports: identity 5090, API 5080,
  client 5070.
- **Calling the API.** The examples use `curl` against the gateway (`http://localhost:5080`). Tenant routes need a
  token for the tenant and the header `X-Tenant`. Scalar, at `http://localhost:5080/scalar`, has every module's
  document if you prefer clicking.
- **Rights.** Nothing in these tutorials goes around the modules' checks. When a call answers 403, the caller is
  missing a permission on the right scope: the [permission summary](/tutorials/newsroom-setup/#who-holds-what) at the
  end of the first tutorial lists who needs what.

:::note[What is built]
Everything these tutorials use is merged, except where a step says otherwise. Two things are only designed:
**conditional visibility** of field values (tutorial 3 shows what to do today), and editing **rich text** in the
studio. Elsa's cutover (E3), which retires the old poller, is still an open pull request.
:::
