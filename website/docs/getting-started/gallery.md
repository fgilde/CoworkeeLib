---
description: Starter specs for coworkee new, a CRM, a helpdesk, a shop and more, to download, plus a small wizard that writes a spec in the browser.
---

# Starter specs

A blank app is fine when you know what goes in. Often it's quicker to start from something close and delete what you don't need. These specs are small on purpose: one or two modules, a handful of entities, an enum or two, sometimes a role. The options are the ones I'd pick for that kind of app. Change them before you create it, nothing here is fixed.

<div class="cw-starters" id="cw-starters">
  <div class="cw-starters__bar">
    <input type="search" placeholder="Search the starters" aria-label="Search the starters" autocomplete="off">
    <div class="cw-starters__tags" role="group" aria-label="Filter by tag"></div>
  </div>
  <div class="cw-starters__grid"></div>
</div>

The same specs ship inside the CLI and in [Coworkee Studio](studio.md) (*Start from a template* on the welcome page), so you don't need to download anything:

```bash
coworkee specs list
coworkee new Sales --template-spec crm
coworkee specs show crm > crm.json   # to change it first
```

## Write your own spec

Pick a starter or start blank, give the app a name and see the spec and the command change as you go. It covers only the switches most people touch first. Everything else (entities, users, branding, the remaining options) goes into the JSON afterwards, or into the Studio, which reads the file too.

<form class="cw-wizard" id="cw-wizard" autocomplete="off">
  <div class="cw-wizard__fields">
    <label>Start from <select name="starter"></select></label>
    <label>Name <input name="name" required spellcheck="false"></label>
    <label>Title <input name="title" placeholder="defaults to the name"></label>
    <label>Database
      <select name="database">
        <option value="postgres">PostgreSQL</option>
        <option value="sqlServer">SQL Server</option>
        <option value="sqlite">SQLite</option>
      </select>
    </label>
    <label>AI assistant <select name="ai"></select></label>
    <fieldset class="cw-wizard__checks">
      <legend>Infrastructure and modules</legend>
      <label><input type="checkbox" name="redis"> Redis</label>
      <label><input type="checkbox" name="keycloak"> Keycloak</label>
      <label><input type="checkbox" name="reports"> Reports</label>
    </fieldset>
    <fieldset class="cw-wizard__checks">
      <legend>Languages</legend>
      <label><input type="checkbox" name="languages" value="en"> en</label>
      <label><input type="checkbox" name="languages" value="de"> de</label>
      <label><input type="checkbox" name="languages" value="fr"> fr</label>
      <label><input type="checkbox" name="languages" value="es"> es</label>
      <label><input type="checkbox" name="languages" value="it"> it</label>
      <label><input type="checkbox" name="languages" value="nl"> nl</label>
      <label><input type="checkbox" name="languages" value="pl"> pl</label>
      <label><input type="checkbox" name="languages" value="pt"> pt</label>
      <label><input type="checkbox" name="languages" value="ja"> ja</label>
      <label><input type="checkbox" name="languages" value="zh"> zh</label>
    </fieldset>
  </div>
  <div class="cw-wizard__out">
    <ul class="cw-problems" id="cw-wizard-problems" aria-live="polite"></ul>
    <pre><code id="cw-wizard-json"></code></pre>
    <div class="cw-line" id="cw-wizard-command"><code></code></div>
    <div class="cw-spec__actions">
      <button type="button" class="cw-btn cw-btn--primary" id="cw-wizard-download">Download spec</button>
      <button type="button" id="cw-wizard-copy">Copy</button>
    </div>
  </div>
</form>

Save the file next to where the app should go and run the command; the app ends up in a folder named like it. The check above runs against the [JSON schema of a spec](../assets/spec.schema.json), the output of `coworkee spec schema`. `coworkee spec validate` knows a few more rules, references between entities for example, and `coworkee new` runs it anyway before it creates anything.


