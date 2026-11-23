# Pregled projekta

Pregledal sem dokumentacijo (docs/01–08), ADR-je in kodo, zagnal teste in aplikacijo lokalno (Docker Compose z Postgresom). Ročno sem preizkusil prijavo, skupine, delitev stroška, poravnavo dolga, odobritev nakazila nad 500 EUR (uslužbenec) in upravljanje dostopov (novega uporabnika sem ustvaril, nato deaktiviral: odjavilo ga je in se ni mogel več prijaviti).

## Implementirane funkcionalnosti MVP

| Funkcionalnost | Zgodba | Stanje |
|---|---|---|
| Prijava in odjava | US-18 | Implementirano |
| Pregled računov in zadnjih 20 nakazil | US-01 | Implementirano |
| Interno nakazilo (nad 500 EUR čaka na odobritev) | US-02 | Implementirano |
| Skupine in povabila | US-03 | Implementirano |
| Delitev stroška | US-04 | Implementirano |
| Potrditev ali ugovor deleža | US-05 | Implementirano |
| Pregled dolgov in terjatev | US-06 | Implementirano |
| Poravnava obveznosti | US-07 | Implementirano |
| Odobritev nakazil (uslužbenec) | US-16 | Implementirano |
| Upravljanje dostopov (administrator) | US-17 | Implementirano |

Kaj manjka: pri MVP zgodbah nisem našel manjkajoče funkcionalnosti. Zgodbe US-08–US-15 (poenostavitev poravnav, delna plačila, proračun, obvestila ...) niso del MVP in niso implementirane. Testa za sočasna plačila se izvedeta samo na PostgreSQL (v CI), lokalno na SQLite se preskočita. Aplikacija še nima nastavitev za produkcijo (seed in migracije tečejo samo v razvoju).

## Ujemanje z uporabniškimi zgodbami

**US-02 – Interno nakazilo:** Nakazilo do 500 EUR se izvede takoj, nad 500 EUR (npr. 500,01) čaka na odobritev in stanja ne spremeni. Neobstoječ prejemnik, isti račun in premalo sredstev vrnejo napako. Ponovitev iste zahteve (isti ključ) ne naredi drugega nakazila.

**US-04 – Delitev stroška:** Datum v prihodnosti se zavrne. Vsota deležev mora biti enaka strošku. Pri enakomerni delitvi 10 EUR na tri člane dobijo 3,34, 3,33 in 3,33 EUR. Dodatni cent dobi član z najnižjim ID-jem, ne plačnik, kar je sicer skladno z zgodbo.

**US-07 – Poravnava obveznosti:** Plačilo lahko sproži samo dolžnik, znesek in prejemnik sta določena z obveznostjo. Po izvedbi je dolg poravnan, med čakanjem na odobritev ostane odprt. Po zavrnitvi ali neuspehu je dovoljen nov poskus. Drugo plačilo istega dolga, dokler prvo čaka, se zavrne.

**US-16 – Odobritev nakazila:** Čakalna vrsta kaže po 20 nakazil od najstarejšega. Zavrnitev zahteva razlog, shrani se uslužbenec in čas. Če ob odobritvi ni dovolj sredstev, nakazilo ne uspe. Že obravnavanega nakazila ni mogoče obravnavati ponovno.

**US-17 – Upravljanje dostopov:** Administrator ustvari uporabnika (e-pošta mora biti enolična, geslo vsaj 12 znakov). Deaktivacija prekine odprto sejo. Zadnjemu aktivnemu administratorju ni mogoče odvzeti dostopa, komitentu z računom pa ni mogoče spremeniti vloge.

## Povzetek

Vseh pet pregledanih zgodb je izpolnjenih. Robne primere (ponovitev zahteve, prag 500 EUR, premalo sredstev, sočasnost) preverja backend, kar pokrivajo tudi integracijski testi (`dotnet test`: 13 uspešnih, `npm test`: 2 uspešna).
