# Tester le workflow install → utilisation → désinstallation

Ce guide reproduit exactement ce que ferait un utilisateur lambda, et vérifie que la
désinstallation ne laisse **aucune trace** sur le PC.

## Ce qui est fourni

| Fichier | Rôle |
|---|---|
| `build-installer.ps1` | Publie le build self-contained + génère l'installateur brandé Guardi (`GuardiSetup-x.y.z.exe`). |
| `installer/Guardi.iss` | Script Inno Setup (branding, raccourcis, Add/Remove Programs, teardown à la désinstallation). |
| `verify-clean.ps1` | Vérifie après désinstallation qu'aucune trace ne subsiste. |

Le build est **self-contained** : le runtime .NET 9 est embarqué, l'utilisateur n'a **rien**
à installer au préalable.

---

## Prérequis (une seule fois, sur ta machine de build)

Inno Setup n'est pas encore installé. Installe-le :

```powershell
winget install JRSoftware.InnoSetup
```

> Sans Inno Setup, `build-installer.ps1` s'arrête après la publication et te laisse un dossier
> `publish\GuardiSetup` que tu peux quand même lancer à la main pour tester l'app.

---

## Étape 1 — Construire l'installateur

Guardi ne doit **pas** être installé/en cours d'exécution pendant le build (sinon la
publication ne peut pas écraser les fichiers).

```powershell
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
```

Résultat : `installer\output\GuardiSetup-0.8.43.exe`

## Étape 2 — Installer (rôle utilisateur lambda)

1. Double-clique sur `GuardiSetup-0.8.43.exe`.
2. Accepte l'élévation UAC (Guardi doit tourner en administrateur).
3. Suis l'assistant (stylé Guardi : mascotte + bleu).
4. À la fin, Guardi se lance et démarre son **assistant de configuration interne** (code
   PIN, mode, limites) — c'est là que se trouve le vrai onboarding stylé Guardi.

L'app apparaît dans **Paramètres Windows → Applications** (et dans « Ajouter/supprimer des
programmes »).

## Étape 3 — Utiliser

Configure un PIN, choisis un mode, teste les protections (limites, filtrage, self-lock, etc.)
comme un utilisateur réel.

## Étape 4 — Désinstaller proprement

Comme n'importe quel logiciel :

- **Paramètres Windows → Applications → Guardi → Désinstaller**, ou
- menu Démarrer → « Désinstaller Guardi ».

À la désinstallation, Inno Setup lance d'abord (dans `InitializeUninstall`, **avant toute
suppression de fichier**) `EduGuardAgent.exe --uninstall`, qui affiche le **gate Guardi** :

1. **Self-lock** : si un self-lock est actif, un message mascotte s'affiche et la
   désinstallation est **refusée** tant que la période n'est pas terminée (code retour 2).
2. **Code PIN** : la fenêtre PIN stylée Guardi s'ouvre ; sans le bon code, la désinstallation
   est **annulée** (code retour 1).
3. **Autorisé** (code retour 0) → alors seulement :
   - arrêt du cluster de processus auto-protégé (signal d'arrêt intentionnel + kill) ;
   - **annulation de tout le footprint système** — hosts, DNS familial, politiques
     Chrome/Edge/Brave, politiques Firefox, native messaging, certificat racine, tâches
     planifiées `GuardiAgent` + `GuardiSystem` ;
   - **suppression de toutes les données** — `ProgramData\EduGuard` et `AppData\EduGuard` de
     **tous les profils** Windows.

Selon le code retour, Inno Setup poursuit (retire les fichiers + l'entrée Add/Remove Programs)
ou **annule** la désinstallation en gardant tout en place.

## Étape 5 — Vérifier « aucune trace »

Dans un PowerShell **administrateur** :

```powershell
powershell -ExecutionPolicy Bypass -File .\verify-clean.ps1
```

Chaque poste est marqué `[clean]` ou `[LEFTOVER]`. Sortie `RESULT: fully clean` = zéro trace.
Le script contrôle : tâches planifiées, dossiers de données (tous profils), certificat racine,
fichier hosts, politiques de navigateur Chromium, et enregistrement native-messaging.

---

## Notes

- **Élévation** : install et désinstall demandent l'administrateur (l'app en a besoin pour
  poser/retirer les protections).
- **Gate de désinstallation** : la désinstallation exige le **code PIN** et est **bloquée
  pendant un self-lock** (voir Étape 4). Le gate s'affiche dans une fenêtre stylée Guardi.
- **DNS** : le rétablissement DNS s'appuie sur l'état sauvegardé ; `verify-clean.ps1` ne
  contrôle pas le DNS automatiquement (dépend de l'adaptateur). Vérifie au besoin via
  `Get-DnsClientServerAddress`.
