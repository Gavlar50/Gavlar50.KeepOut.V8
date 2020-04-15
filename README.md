# Gavlar50.KeepOut

KeepOut uses Umbraco member groups to block access to content. Where both a member group grants access, and a KeepOut rule denies access, the KeepOut rule wins.

## V3.0.0
* Refactored for Umbraco 8.
* Rule visualisation default changed to on.
* Removed logging, as the visualisation will identify when a rule has been applied.
* Restyled the rule visualisation to make it more subtle.

## V2.0.1 for Umbraco 7
* KeepOut for Umbraco 7 is archived and no longer maintained, but is available here: https://github.com/Gavlar50/Gavlar50.KeepOut


## Installation
The install creates a KeepOut Security Rules node in the root of the site. This node contains the configuration. Add rules under this node. Each rule contains the following settings:
* Page to secure. This page and all children are secured.
* No access page. The page to redirect to when the member has no access
* Member groups. One or more member groups who are denied access
* Rule colour. The colour used to visualise the rule in the node tree (when visualisation is enabled)
