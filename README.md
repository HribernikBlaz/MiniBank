# MiniBank

![CI/CD](https://github.com/HribernikBlaz/MiniBank/actions/workflows/ci-cd.yml/badge.svg)

**Aplikacija:** https://minibank-hribernik-aba7c9aef5fhfzhm.austriaeast-01.azurewebsites.net/

Učna simulacija skupnostnega bančništva z izmišljenimi podatki in EUR.

MVP pokriva US-01-US-07 in US-16-US-18. Komitent lahko pregleda račune, odda nakazilo, sodeluje v skupini, razdeli strošek in poravna obveznost. Uslužbenec obravnava nakazila nad 500 EUR, administrator pa uporabniške dostope.

[MVP Board](https://github.com/users/JakaM1/projects/3) · [Načrt in preverjanje](docs/08-implementacija-mvp.md) · [Uporabniške zgodbe](docs/05-uporabniske-zgodbe.md) · [Arhitektura](docs/04-arhitektura-in-tehnoloski-sklad.md) · [ADR](docs/adr/README.md)

## CI/CD cevovod

Cevovod je v `.github/workflows/ci-cd.yml` (GitHub Actions). Zažene se ob PR v `main` in ob pushu v `main`. Veja `main` je zaščitena: merge je mogoč samo prek PR, ko uspejo `lint`, `build` in `test`.

1. **lint** - preveri slog kode: `dotnet format --verify-no-changes` (backend) in ESLint (frontend).
2. **build** - sestavi aplikacijo: `dotnet publish` in `vite build`. Rezultat shrani kot artefakta `backend` in `frontend`. NuGet in npm sta v cache-u.
3. **test** - 26 backend testov (SQLite in PostgreSQL) in 6 frontend testov, skupaj 32. Preveri tudi migracije in naloži poročilo o pokritosti (`coverage-report`).
4. **deploy** - teče samo ob pushu v `main`, ko uspejo vsi trije prejšnji jobi. Vzame artefakta iz `build` in ju objavi na Azure App Service (podatki v Neon PostgreSQL). Skrivnosti so v GitHub environmentu `production`.

## Lokalni zagon

Potrebujete .NET SDK 10.0.100 (ali novejši popravek 10.0.1xx), Node.js 22.14+ in Docker Compose z delujočim pogonom. Ukaze izvajajte v korenu repozitorija.

1. Kopirajte `.env.example` v `.env` in nastavite lokalno geslo baze.
2. Zaženite bazo in pripravite konfiguracijo. V povezovalnem nizu uporabite isto geslo kot v `.env`; `Seed:Password` je vaše lokalno demonstracijsko geslo z vsaj 12 znaki.

```sh
docker compose up -d db
dotnet tool restore
dotnet restore --locked-mode --configfile NuGet.Config
dotnet user-secrets set "ConnectionStrings:MiniBank" "Host=localhost;Database=minibank;Username=minibank;Password=VASE_GESLO_BAZE" --project backend/MiniBank.Api
dotnet user-secrets set "Seed:Password" "VASE_DEMONSTRACIJSKO_GESLO" --project backend/MiniBank.Api
dotnet user-secrets set "Seed:Enabled" "true" --project backend/MiniBank.Api
dotnet ef database update --project backend/MiniBank.Api
dotnet run --project backend/MiniBank.Api --launch-profile Development
```

3. V drugem terminalu:

```sh
cd frontend
npm ci
npm run dev
```

Odprite [localhost:5173](http://localhost:5173). Razvojni inicializator v prazni bazi pripravi `ana@minibank.test`, `bor@minibank.test`, `cene@minibank.test`, `usluzbenec@minibank.test` in `admin@minibank.test` z vašim `Seed:Password`. Trije demonstracijski komitenti dobijo po 200 EUR. Povabila se prikažejo v aplikaciji uporabniku z vnesenim e-poštnim naslovom.

Za preizkus skupinske poravnave naj Ana ustvari skupino in povabi Bora. Ko Bor sprejme povabilo, naj Ana razdeli strošek 60 EUR na enaka deleža in odda predlog. Bor potrdi delež in v razdelku **Obveznosti skupine** poravna 30 EUR.

Ponovni zagon API zahteva novo prijavo in ohrani podatke. `docker compose stop` ohrani bazo. Namesto Dockerja lahko uporabite lokalni PostgreSQL 17 in prilagodite povezovalni niz. Backend `.env` ne bere; uporablja user-secrets ali okoljske spremenljivke.

## Preverjanje in razvoj

```sh
dotnet test
cd frontend
npm test
npm run build
```

API testi privzeto uporabljajo izolirano SQLite bazo. Z okoljsko spremenljivko `MINIBANK_TEST_POSTGRES` (povezovalni niz lokalne testne baze in uporabnik s pravico `CREATEDB`) iste teste izvedete na PostgreSQL; za vsak test se ustvari in odstrani ločena baza `minibank_tests_*`. CI preveri oba ponudnika in migracije. Sprejemni scenariji so v [evidenci](docs/08-implementacija-mvp.md).

Za vsako spremembo ustvarite razvojno vejo in PR z opisom, preverjanji ter `Closes #številka`. Nalogo premaknite `To Do` → `Doing` → `Done` po uspešnem preverjanju in združitvi. Podrobnosti: [GitHub Flow](docs/07-razvojni-postopek.md). Lokalnih gesel in `.env` ne vključujte v Git.
