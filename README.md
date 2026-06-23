# EduGuard Agent (Windows)

Application Windows **plein écran** pour le Sub — profil **College Student**.

## Lancer (admin requis pour bloquer des sites)

Le blocage web modifie le fichier `hosts` et écoute les ports **80 et 443**. **Lance l'app en administrateur** :

```powershell
cd C:\Users\vferr\Projects\EduGuardAgent
dotnet run
```

Windows peut demander une élévation UAC au démarrage.

## Bloquer des sites (Dom → Sub)

1. Depuis le panneau Dom Lovable, envoie la commande `block_url` avec `{ "host": "reddit.com" }`
2. L'agent ajoute le site à la blocklist, met à jour `hosts`, et démarre le serveur de page de blocage
3. Le Sub voit le site dans **Your current restrictions**
4. En ouvrant le site, une **page EduGuard violette** s'affiche (HTTP et HTTPS)

Déblocage : commande `unblock_url` avec le même host.

## Page de blocage (HTTP + HTTPS)

Style EduGuard (violet, bouclier, niveau College Student). Le domaine bloqué est affiché clairement.

L'agent installe une autorité de certification locale **EduGuard** dans Windows (au premier blocage). Elle permet la page personnalisée en **HTTPS** sur Chrome/Edge.

**Firefox** : dans `about:config`, mets `security.enterprise_roots.enabled` à `true` pour utiliser les certificats Windows.

## Test rapide

```
# Depuis Lovable Dom
block_url → { "host": "example.com" }

# Sur le PC Sub, ouvre dans le navigateur :
https://example.com
```

## Autres fonctionnalités

- Enrollment, heartbeat, commandes (`show_message`, `lock_screen`, `kill_process`, `block_app`)
- Screen time + overlay de verrouillage (3 min en mode test)
- Bouton « Quitter le verrouillage » (mot de passe plus tard)

## Fichiers locaux

- Token : `%AppData%\EduGuard\agent.dat`
- Sites bloqués : `%AppData%\EduGuard\blocked_hosts.json`
- Audit : `%AppData%\EduGuard\audit.log`
