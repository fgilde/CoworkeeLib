---
description: Startvorlagen für coworkee new, ein CRM, ein Helpdesk, ein Shop und mehr, zum Herunterladen, dazu ein kleiner Assistent, der eine Spec im Browser schreibt.
---

# Startvorlagen

Eine leere App passt, wenn du schon weißt, was hinein soll. Oft geht es schneller, mit etwas Ähnlichem anzufangen und zu löschen, was du nicht brauchst. Die Specs hier sind absichtlich klein: ein oder zwei Module, eine Handvoll Entities, ein, zwei Enums, manchmal eine Rolle. Die Optionen sind die, die ich für so eine App wählen würde. Ändere sie, bevor du die App anlegst, festgelegt ist hier nichts.

<div class="cw-starters" id="cw-starters">
  <div class="cw-starters__bar">
    <input type="search" placeholder="Vorlagen durchsuchen" aria-label="Vorlagen durchsuchen" autocomplete="off">
    <div class="cw-starters__tags" role="group" aria-label="Nach Tag filtern"></div>
  </div>
  <div class="cw-starters__grid"></div>
</div>

Dieselben Specs stecken auch im CLI und in [Coworkee Studio](studio.md) (*Mit einer Vorlage starten* auf der Startseite), herunterladen musst du also nichts:

```bash
coworkee specs list
coworkee new Sales --template-spec crm
coworkee specs show crm > crm.json   # um sie vorher anzupassen
```

## Eigene Spec schreiben

Nimm eine Vorlage oder fang leer an, gib der App einen Namen und sieh zu, wie sich Spec und Befehl mitändern. Der Assistent deckt nur die Schalter ab, die die meisten zuerst anfassen. Alles andere (Entities, Benutzer, Branding, die übrigen Optionen) kommt danach ins JSON oder ins Studio, das die Datei ebenfalls liest.

<form class="cw-wizard" id="cw-wizard" autocomplete="off">
  <div class="cw-wizard__fields">
    <label>Vorlage <select name="starter"></select></label>
    <label>Name <input name="name" required spellcheck="false"></label>
    <label>Titel <input name="title" placeholder="sonst der Name"></label>
    <label>Datenbank
      <select name="database">
        <option value="postgres">PostgreSQL</option>
        <option value="sqlServer">SQL Server</option>
        <option value="sqlite">SQLite</option>
      </select>
    </label>
    <label>KI-Assistent <select name="ai"></select></label>
    <fieldset class="cw-wizard__checks">
      <legend>Infrastruktur und Module</legend>
      <label><input type="checkbox" name="redis"> Redis</label>
      <label><input type="checkbox" name="keycloak"> Keycloak</label>
      <label><input type="checkbox" name="reports"> Berichte</label>
    </fieldset>
    <fieldset class="cw-wizard__checks">
      <legend>Sprachen</legend>
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
      <button type="button" class="cw-btn cw-btn--primary" id="cw-wizard-download">Spec herunterladen</button>
      <button type="button" id="cw-wizard-copy">Kopieren</button>
    </div>
  </div>
</form>

Speichere die Datei dort, wo die App hin soll, und führe den Befehl aus; die App landet in einem Ordner mit ihrem Namen. Die Prüfung oben läuft gegen das [JSON-Schema einer Spec](../assets/spec.schema.json), also die Ausgabe von `coworkee spec schema`. `coworkee spec validate` kennt noch ein paar Regeln mehr, etwa zu Referenzen zwischen Entities, und `coworkee new` ruft es sowieso auf, bevor es irgendetwas anlegt.


