# Journal des pointages Factorial HR

Application pour le projet Le Chameau SCZ Morocco. Elle réunit les pointages importés de Factorial HR et les saisies locales dans un journal filtrable, avec une interface React, une API ASP.NET Core et une base SQL Server.

**[Présentation de la solution (PDF)](output/pdf/presentation-factorial-pointage.pdf)** · **[Extension Factorial Code](factorial-code/README.md)**

Le journal affiche le matricule, l’email, le nom, la date, les heures d’entrée et de sortie et la durée écoulée. Un pointage ouvert ou une heure ambiguë affiche `—` dans la colonne de durée.

L’espace **Paramétrage** permet de tester la connexion, régler les imports automatiques et consulter l’annuaire Factorial ainsi que les dernières opérations. Le pointage local et le filtre du journal utilisent les collaborateurs lus dans Factorial. **Ajouter un pointage** accepte les dates et heures locales d’entrée et de sortie, y compris après minuit. Les pointages de l’application se modifient ou se retirent depuis **Actions**. Une suppression retire la ligne du journal et conserve son enregistrement dans SQL Server. **Toutes les dates** donne accès à l’historique local paginé.

## Installation et démarrage sur Windows

Installer PowerShell 7, Node.js 24 et le SDK .NET 10, puis cloner le dépôt.

```powershell
git clone https://github.com/JENNAH-IMAD/factorielapp.git
cd factorielapp
```

### Essayer avec des données fictives

```powershell
pwsh -File scripts/Start-Demo.ps1
```

Ouvrir [la démonstration](http://127.0.0.1:5080/) et utiliser le bouton d’accès à la démonstration. Le mode démo utilise une base en mémoire et des réponses Factorial simulées. Les modifications disparaissent au redémarrage.

### Connecter SQL Server et Factorial

Installer SQL Server ou disposer d’une instance accessible avec l’authentification Windows. Par défaut, le script utilise `.\SQLEXPRESS` et la base `API_Sage_Factorial`.

```powershell
pwsh -File scripts/Start-Real.ps1 -ConnectFactorial
```

Au premier lancement, choisir le mot de passe administrateur, puis saisir la clé Factorial à l’invite masquée. Ouvrir [le journal SQL Server](http://127.0.0.1:5081/) et se connecter avec l’identifiant `admin` et le mot de passe choisi. Pour une autre instance, ajouter `-Instance 'SERVEUR\INSTANCE'`. Le compte exécutant les migrations doit disposer des droits nécessaires. Pour un déploiement serveur, configurer la connexion SQL côté serveur et servir l’application en HTTPS.

## Utiliser le journal

1. Dans **Paramétrage**, tester la connexion Factorial et vérifier la synchronisation de l’annuaire. Le réglage de la fréquence est optionnel.
2. Dans **Journal**, choisir les dates et, si besoin, un collaborateur. Utiliser **Importer depuis Factorial** pour actualiser la période. **Toutes les dates** ouvre l’historique paginé.
3. Cliquer sur **Ajouter un pointage**, puis choisir **Saisie manuelle** pour renseigner les dates et heures, ou **En temps réel** pour enregistrer les événements au fil de la journée. Le menu **Actions** permet de modifier ou retirer les saisies de l’application.

L’application consulte Factorial en lecture seule. Les saisies faites dans le journal restent dans la base locale. Garder les clés et mots de passe dans la configuration du serveur.

## Essayer l’onglet Factorial Code

```powershell
cd factorial-code
npm run preview
```

Ouvrir [l’aperçu](http://127.0.0.1:5082/) et afficher les pointages du collaborateur fictif `42`. L’installation de l’onglet dans Factorial HR nécessite un espace Factorial Code approuvé. [Procédure de déploiement](factorial-code/README.md#installation-en-démonstration)

## Table et journal SQL

La table `dbo.Pointage_factorial` expose `Identification varchar(10)`, `Check_in datetime` et `Check_out datetime NULL`. La clé `Id` est générée automatiquement. L’identification garde ses éventuels zéros initiaux. Les valeurs de cette table représentent des heures locales du Maroc. Utiliser le format ISO pour éviter une interprétation qui dépend de la langue de la session SQL Server.

```sql
INSERT INTO dbo.Pointage_factorial (Identification, Check_in, Check_out)
VALUES ('0419', CONVERT(datetime, '2026-11-16T08:30:00', 126),
               CONVERT(datetime, '2026-11-16T18:00:00', 126));

SELECT Identification, Check_in, Check_out FROM dbo.Pointage_factorial;
```

`dbo.vw_Journal_Pointages` réunit les saisies SQL, les pointages de l’application et les pointages lus dans Factorial. `dbo.Attendance` conserve les horodatages, identifiants distants et informations de synchronisation des pointages gérés par l’application. Une ligne SQL est enrichie par un matricule exact et unique, ou par l’identifiant Factorial si aucun matricule ne correspond. Le nom et l’email restent vides en l’absence d’une correspondance fiable. La date affichée est celle du début ou la date d’imputation fournie par Factorial.

Les migrations EF sont dans `backend/Migrations`. `docs/migrations.sql` permet de les appliquer à une base SQL Server existante. `docs/exemple-pointage.sql` exécute l’exemple dans une transaction annulée afin de ne pas polluer le journal réel.

## Import Factorial

Le backend utilise l’API officielle `2026-10-01`. Le bouton **Importer depuis Factorial** lit l’annuaire, puis les pointages de la période sélectionnée. Fournir une nouvelle clé dans la configuration serveur `Factorial:ApiKey`, par exemple via la variable `Factorial__ApiKey`. Pour saisir la clé à l’invite masquée et la conserver uniquement pendant la session serveur, lancer `pwsh -File scripts/Start-Real.ps1 -ConnectFactorial`. Pour un import ponctuel des 90 derniers jours sans stockage de la clé :

```powershell
pwsh -File scripts/Start-Real.ps1 -ImportOnly
```

La première intégration crée une connexion pour Factorial Production. Pour une entreprise hébergée sur l’environnement de démonstration de Factorial, utiliser une autre base SQL et la configuration `Factorial__Environment=Demo` avant son premier démarrage. Le mode de démonstration local est un simulateur séparé.

Sous Windows, pour conserver la clé de production entre deux lancements, exécuter `pwsh -File scripts/Set-FactorialKey.ps1`. La clé est saisie à l’invite masquée, vérifiée en lecture seule pour l’entreprise liée à la base et stockée chiffrée dans le profil Windows du compte qui lance l’application. Une clé configurée explicitement côté serveur continue de prendre la priorité. La clé sauvegardée n’est ni renvoyée à React ni utilisée pour l’environnement de démonstration.

## Validation

```powershell
dotnet test tests/Pointage.Tests.csproj
cd frontend
npm ci
npm run build
npm test
```

Pour vérifier la migration, la vue, les contraintes et les verrous sur SQL Server, définir `POINTAGE_TEST_DATABASE` vers une base de test isolée avant de lancer `dotnet test`. Le test crée son schéma et des lignes de test. Les tests du connecteur n’appellent pas Factorial.
