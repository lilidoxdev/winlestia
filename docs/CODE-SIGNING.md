# Signature de code pour Winlestia

Document de travail rédigé le 17 novembre 2026. Les prix et conditions changent souvent : chaque chiffre est daté et sourcé. Les points marqués **[non vérifié]** n'ont pas pu être confirmés sur une source officielle à cette date.

Contexte : Winlestia est distribué par un développeur individuel en France (pas de société), sous licence GPL-3.0. Le nouvel installateur `WinlestiaSetup.exe` (WPF, .NET 8) n'est pas signé ; Windows affiche « Windows a protégé votre ordinateur » et « Éditeur : inconnu ».

---

## 1. Résumé

**Ce que la signature change, et ce qu'elle ne change pas.** Signer remplace « éditeur inconnu » par un nom vérifié et garantit que le fichier n'a pas été modifié. Elle **ne supprime pas** à elle seule l'écran SmartScreen : depuis 2024, aucun certificat (même EV) ne donne de réputation immédiate. La réputation s'accumule avec les téléchargements sans incident, sur le hash du fichier et sur l'identité du certificat. Microsoft parle de « plusieurs semaines et centaines d'installations propres ».

**Ordre conseillé :**

1. **SignPath Foundation (gratuit)** si le projet passe l'examen. Plusieurs points du dépôt sont à régler avant de candidater (voir 4.1) : build de release sur GitHub Actions, binaires tiers non signables par SignPath, statut des fonds d'écran. L'éditeur affiché sera « SignPath Foundation », pas votre nom ni « Winlestia ».
2. **Azure Artifact Signing (ex-Trusted Signing) : non disponible pour vous.** La documentation Microsoft (mise à jour le 17/11/2026) limite les particuliers aux États-Unis et au Canada. Un particulier en France ne peut pas faire valider son identité. À réévaluer si Microsoft ouvre l'UE aux particuliers, ou si vous créez une structure (les organisations de l'UE sont acceptées).
3. **Certum Open Source Code Signing** comme solution de repli, ou en attendant SignPath. Ce certificat est réservé aux personnes physiques, ce qui correspond à votre situation. Prix constatés sur la boutique Certum le 17/11/2026 : 49 € TTC (clé dans le cloud SimplySign) ou 69 € TTC (carte à puce + lecteur). L'éditeur affiché sera « Open Source Developer, <votre nom> ».
4. **Microsoft Store : pas une voie réaliste à court terme** pour un shell qui remplace la barre des tâches (voir 4.4). Le chemin MSIX (signé gratuitement par Microsoft) demande `runFullTrust`, une capacité restreinte soumise à approbation. Le chemin EXE/MSI exige de toute façon un installateur déjà signé par vous.
5. **winget** : aucune signature exigée par la politique du dépôt. C'est utile en complément, mais SmartScreen peut quand même avertir à l'exécution.

**Coûts annuels estimés :**

| Option | Coût annuel | Délai de mise en place |
|---|---|---|
| SignPath Foundation | 0 € | Examen manuel, durée non publiée **[non vérifié]**, compter plusieurs semaines |
| Certum Open Source, cloud | 49 € TTC/an (boutique, 17/11/2026) | Quelques jours ouvrés après envoi des pièces **[délai non vérifié]** |
| Certum Open Source, carte | 69 € TTC la 1re année, carte et lecteur compris ; renouvellement moins cher **[non vérifié]** | Idem, plus la livraison depuis la Pologne |
| Azure Artifact Signing | ~9,99 $/mois, soit ~120 $/an (~110 €) **[conversion approximative]** | Inaccessible aux particuliers en France |
| OV classique (Sectigo, DigiCert, SSL.com…) | 150 à 300 $/an selon Microsoft ; plus un token ou un abonnement de signature cloud | Plusieurs jours ouvrés |
| EV | 400 $/an et plus ; aucun avantage SmartScreen depuis 2024 | Non pertinent |

---

## 2. Règles en vigueur en 2026

- **Clé privée sur matériel.** Depuis juin 2023, les exigences de base du CA/Browser Forum imposent que la clé privée d'un certificat de signature de code OV soit stockée sur un HSM ou un token matériel (FIPS 140-2 niveau 2 / Common Criteria EAL4+). Il n'est plus possible de recevoir un fichier `.pfx` à garder sur disque. Les options sont : token USB, carte à puce, HSM cloud (SimplySign, eSigner, Azure Key Vault…) ou service géré (Azure Artifact Signing, SignPath).
- **Durée maximale.** Le ballot CSC-31 (Code Signing Baseline Requirements v3.10.0, adopté le 17/11/2025) ramène la durée maximale d'un certificat de signature de code à **460 jours**, pour tout certificat émis à partir du 1er mars 2026. Les certificats émis avant gardent leur échéance. Certaines AC appliquent un plafond légèrement inférieur : SSL.com indique 458 jours à partir du 27/02/2026, Certum 459 jours. Concrètement, il faut renouveler chaque année.
- **Fin de l'avantage EV.** Le programme Microsoft Trusted Root a retiré les OID EV des racines à partir d'août 2024 : « all Code Signing certificates will be treated equally ». Microsoft écrit que payer l'EV « solely to avoid SmartScreen warnings is no longer justified ».
- **Horodatage.** Sans horodatage RFC 3161, une signature devient invalide à l'expiration du certificat. Avec horodatage, elle reste valide. Avec des certificats d'un an, horodater est indispensable.

---

## 3. Tableau comparatif

| Critère | SignPath Foundation | Azure Artifact Signing | Certum Open Source | OV classique (Sectigo / SSL.com / DigiCert) | Microsoft Store |
|---|---|---|---|---|---|
| Coût | Gratuit | ~9,99 $/mois (Basic : 5 000 signatures/mois) | 49 € (cloud) ou 69 € (carte) TTC, la 1re année | 150 à 300 $/an, plus le token ou la signature cloud (SSL.com eSigner : 20 $/mois par identifiant supplémentaire) | Compte individuel gratuit |
| Particulier en France | Oui : c'est le projet qui est éligible, pas la personne | **Non** (particuliers : USA/Canada seulement) | **Oui**, uniquement pour les personnes physiques | IV (« Individual Validation ») possible chez SSL.com et d'autres **[prix et pièces non vérifiés]** | Oui |
| Nom affiché comme éditeur | « SignPath Foundation » | Votre nom | « Open Source Developer, <nom> » | Votre nom | Nom du compte Store |
| Matériel | Aucun (HSM géré par SignPath) | Aucun (géré par Microsoft) | Cloud SimplySign (appli mobile pour les codes) ou carte + lecteur | Token USB FIPS ou HSM cloud | Aucun (MSIX) / certificat requis (EXE/MSI) |
| Effet SmartScreen | Réputation progressive. Partage-t-elle la réputation des autres projets signés par la fondation ? **[non vérifié]** | Réputation progressive | Réputation progressive | Réputation progressive | Pas d'avertissement SmartScreen à l'installation depuis le Store |
| Intégration CI | Native : action GitHub `signpath/github-action-submit-signing-request@v3`, runners hébergés par GitHub obligatoires pour l'OSS | Native : action GitHub, Azure DevOps | Difficile : SimplySign demande un code à usage unique depuis le téléphone ; la carte demande un poste physique | eSigner (SSL.com) automatisable ; token USB = poste physique | Hors sujet |
| Contraintes particulières | Build vérifiable depuis le dépôt, approbation manuelle de chaque release, MFA, politique de signature publiée, aucun binaire tiers | Validation d'identité de 1 à 30 jours ouvrés | Justificatif de domicile, URL du projet | Validation d'identité | `runFullTrust` restreint ; silencieux obligatoire pour EXE/MSI |

---

## 4. Détail par option

### 4.1 SignPath Foundation

Conditions (https://signpath.org/terms, consulté le 17/11/2026) :

- Licence approuvée par l'OSI pour **tous** les composants, sans double licence commerciale ; aucun composant propriétaire.
- Projet maintenu, déjà publié sous la forme à signer, fonctionnalités décrites sur la page de téléchargement.
- Signature réservée aux binaires construits à partir du code du projet, par un build vérifiable. Pour l'open source, tous les jobs du workflow GitHub menant à la demande de signature doivent tourner sur des runners hébergés par GitHub.
- Les bibliothèques amont doivent être signées par leurs propres mainteneurs.
- Approbation manuelle de chaque release ; MFA obligatoire sur SignPath et GitHub ; rôles définis (Authors, Reviewers, Approvers).
- Page « Code signing policy » publiée sur la page d'accueil et la page de téléchargement, avec la mention SignPath, les rôles et une déclaration sur la vie privée.
- Respect de l'utilisateur : annoncer les modifications système et fournir une désinstallation. Un shell qui remplace la barre des tâches modifie le système : l'installateur doit le dire clairement et la désinstallation doit tout restaurer.
- Logiciels malveillants, PUP et outils d'intrusion exclus. La fondation n'accepte aucune responsabilité.

**Points à régler dans le dépôt avant de candidater** (constatés le 17/11/2026) :

- Les releases sont construites en local (`installer/build.ps1`, `installer/Deploy-Local.ps1`). `.github/workflows/ci.yml` ne fait que build et test. Il faut un workflow de release qui produise `WinlestiaSetup.exe` et le payload sur `windows-latest`.
- Le payload contient des binaires tiers : runtime Python/PySide6/Qt, `clink_x64.exe` (ajouté à la construction, ignoré par git). SignPath ne les signera pas. Seuls les binaires construits depuis vos sources (backend .NET, DLL natives `src/backend/*.cpp`, installateur) peuvent l'être. Les binaires tiers restent avec leur signature d'origine, ou sans signature s'ils n'en ont pas **[état de signature des DLL Qt/PySide non vérifié]**.
- `THIRD_PARTY_NOTICES.md` indique que les fonds d'écran de `assets/wallpapers/` ne sont pas couverts par la GPL et que leur licence n'est pas établie. Ce point risque de faire échouer la condition « licence OSI pour tous les composants », et c'est de toute façon un risque juridique à part. Mieux vaut le régler avant de candidater : retirer ces images ou les remplacer par des images sous licence libre.
- Développeur seul : un même compte peut-il tenir les rôles Author, Reviewer et Approver ? **[non vérifié]**

Délai : non publié. Le formulaire est sur https://signpath.org/apply (la page n'a pas pu être lue en détail).

### 4.2 Azure Artifact Signing (ex-Trusted Signing)

- Nom actuel : « Artifact Signing » (documentation `learn.microsoft.com/azure/artifact-signing`, service GA pour les organisations USA/Canada/Europe en janvier 2026 selon DevClass).
- Éligibilité, citation de la page de démarrage rapide (ms.date 21/05/2026, mise à jour le 17/11/2026) : « Public Trust certificates are available to organizations in the United States, Canada, the European Union, the United Kingdom, […]. Individual developers must be located in the United States or Canada. » Un fil Microsoft Q&A de mars 2026 confirme qu'un développeur français n'a pas pu choisir la France dans le formulaire « Individual ».
- Prix : ~9,99 $/mois selon la page Microsoft « Code signing options » (ms.date 29/10/2026). La page de tarification Azure affiche des valeurs vides ce jour-là ; seuls les quotas sont lisibles (Basic : 5 000 signatures/mois et 1 profil de chaque type ; Premium : 100 000 signatures/mois). Facturation dès la création du compte. Le prix exact en euros n'a pas pu être lu **[non vérifié]**.
- SmartScreen : pas de confiance immédiate ; la réputation de l'éditeur se construit sur les versions successives.
- Conclusion : **inaccessible tant que vous restez un particulier en France.** Une micro-entreprise compte-t-elle comme « organization » ? La validation « Organization » demande un identifiant d'entreprise, un domaine et une adresse e-mail sur ce domaine. L'acceptation d'un entrepreneur individuel n'est pas confirmée ; un témoignage sur le forum JUCE parle de refus pour des entreprises individuelles européennes **[non vérifié]**.

### 4.3 Certum Open Source Code Signing

- Prix sur la boutique, relevés le 17/11/2026 : « Open Source Code Signing on SimplySign » 49,00 € TTC (5 000 signatures/mois, clé dans le cloud, appli mobile SimplySign + SimplySign Desktop) ; « Open Source Code Signing - set » 69,00 € TTC (carte cryptoCertum 3.7 + lecteur). Une autre page de la boutique affiche « à partir de 359 € » à côté du même produit : affichage ambigu **[non vérifié]**. Durée : 1 an selon des sources tierces, plafond Certum de 459 jours.
- Éligibilité (support Certum, « Code Signing – required documents ») : émis **uniquement à des personnes physiques** ; le demandeur doit être l'abonné ; aucun nom de domaine dans le certificat ; « Open Source Developer » ajouté au CN, champ O = « Open Source Developer ».
- Pièces : vérification d'identité (pièce d'identité, plusieurs méthodes au choix lors de l'activation ; la liste exacte n'a pas été lue **[non vérifié]**), justificatif de domicile à votre nom (facture d'eau, de gaz, d'électricité ou de téléphone), adresse web du projet open source en cours (le dépôt GitHub suffit a priori **[non vérifié]**). Il n'y a plus de vérification par téléphone depuis 2017.
- Limite pour la CI : SimplySign demande un code à usage unique depuis le téléphone pour ouvrir une session. On signe donc sur votre poste au moment de la release. Une automatisation complète dans GitHub Actions n'est pas prévue par Certum **[contournements tiers non évalués]**.

### 4.4 Microsoft Store

- Compte développeur individuel gratuit depuis le 10/09/2025 (blog Windows Developer) ; inscription sur https://storedeveloper.microsoft.com.
- **MSIX** : Microsoft re-signe le paquet après certification, gratuitement. Mais une application Win32 empaquetée en MSIX doit déclarer `runFullTrust`, une capacité restreinte qui doit être justifiée lors de la soumission. Le Store accepte-t-il un remplaçant de barre des tâches ou de shell (hooks globaux, masquage de la barre des tâches, démarrage automatique, composants natifs) ? La politique ne le dit pas explicitement **[non vérifié, risque de refus élevé]**. MSIX virtualise aussi une partie du registre et du système de fichiers, ce qui peut gêner un shell.
- **EXE/MSI** (accepté depuis juin 2021) : « The binary and all of its Portable Executable (PE) files must be digitally signed with a code signing certificate that chains up to […] the Microsoft Trusted Root Program ». Microsoft ne re-signe pas. Il faut aussi : une URL versionnée immuable, une installation silencieuse (seule l'invite UAC est tolérée) et un installateur hors ligne. Le Store ne règle donc pas le problème de certificat ; il vient après.

### 4.5 winget

La politique de `microsoft/winget-pkgs` (`doc/Policies.md`) n'exige pas de signature Authenticode. Elle exige des URL officielles « discoverable on the publishers website » (une release GitHub convient a priori), des URL uniques par version de préférence, et le passage des analyses de sécurité du pipeline de validation. Un installateur non signé est donc accepté, mais SmartScreen et la Marque du Web s'appliquent toujours selon la façon dont le fichier est téléchargé **[comportement exact avec winget non vérifié]**.

---

## 5. Réputation SmartScreen et faux positifs

- Deux signaux selon Microsoft : la réputation du **fichier** (hash) et celle de l'**éditeur** (certificat). Sans signature, chaque nouvelle version repart de zéro. Avec une identité stable, la réputation acquise aide les versions suivantes, mais un nouveau build peut encore déclencher l'avertissement tant que son hash ou le certificat n'a pas assez d'historique. Un renouvellement de certificat peut faire perdre une partie de la réputation (témoignages Microsoft Q&A).
- Il n'y a pas de seuil publié : « several weeks and hundreds of clean installs ».
- Bonnes pratiques : signer **chaque** release avec la même identité ; ne jamais modifier un fichier après signature ; éviter de multiplier les builds publics (chaque hash repart à zéro) ; ne pas signer de binaire au comportement douteux, car la réputation négative s'attache au certificat.
- Soumission à Microsoft : https://www.microsoft.com/wdsi/filesubmission. La page Microsoft sur la réputation SmartScreen dit qu'il n'existe pas de mécanisme de soumission pour accélérer la réputation côté grand public. Le formulaire sert à signaler un **faux positif de Microsoft Defender** (détection antivirus), ce qui est un problème différent. À utiliser si Defender détecte `WinlestiaSetup.exe` ou un binaire du payload, en tant que « Software developer ».

---

## 6. Étapes concrètes

### 6.1 Principes communs (quelle que soit l'option)

1. **Ce qu'on signe** : `WinlestiaSetup.exe`, les `.exe` et `.dll` du backend .NET (assemblées Winlestia), les DLL et EXE natifs construits depuis `src/backend/*.cpp`, et tout lanceur `.exe` propre au projet.
2. **Ce qu'on ne signe pas** : les binaires tiers (python.exe, DLL Qt/PySide6, `clink_x64.exe`, runtime .NET). Les signer avec votre certificat mettrait votre nom sur du code que vous n'avez pas écrit, et SignPath l'interdit.
3. **Ordre** : signer le payload, puis construire l'installateur qui l'embarque, puis signer l'installateur en dernier. Toute modification après signature casse la signature et la réputation du hash.
4. **Algorithme** : SHA-256 pour le condensat du fichier (`/fd sha256`) et pour l'horodatage (`/td sha256`).
5. **Vérification** : `signtool verify /pa /all /v <fichier>` sur chaque fichier signé, dans la CI avant publication.

### 6.2 Option recommandée : SignPath via GitHub Actions

Prérequis côté dépôt : workflow de release reproductible sur `windows-latest`, MFA sur GitHub, page « Code signing policy », règlement des fonds d'écran (voir 4.1).

1. Candidater sur https://signpath.org/apply. Une fois accepté, créer dans SignPath le projet, une *artifact configuration* qui liste les fichiers à signer dans l'archive (vos `.exe`/`.dll` et l'installateur), et une *signing policy* « release » avec approbation manuelle.
2. Stocker `SIGNPATH_API_TOKEN` dans les secrets GitHub.
3. Workflow en deux passes (payload, puis installateur) :

```yaml
name: release
on:
  push:
    tags: ['v*']
jobs:
  build:
    runs-on: windows-latest          # runners hébergés par GitHub exigés pour l'OSS
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: 8.0.x }
      # ... construire le backend, les DLL natives, le payload ...
      - id: up-payload
        uses: actions/upload-artifact@v4
        with: { name: payload-unsigned, path: out/payload }
      - uses: signpath/github-action-submit-signing-request@v3
        with:
          api-token: ${{ secrets.SIGNPATH_API_TOKEN }}
          organization-id: '<id organisation SignPath>'
          project-slug: 'winlestia'
          signing-policy-slug: 'release'
          artifact-configuration-slug: 'payload'
          github-artifact-id: ${{ steps.up-payload.outputs.artifact-id }}
          wait-for-completion: true
          output-artifact-directory: out/payload-signed
      # ... construire WinlestiaSetup.exe à partir de out/payload-signed ...
      - id: up-setup
        uses: actions/upload-artifact@v4
        with: { name: setup-unsigned, path: out/WinlestiaSetup.exe }
      - uses: signpath/github-action-submit-signing-request@v3
        with:
          api-token: ${{ secrets.SIGNPATH_API_TOKEN }}
          organization-id: '<id organisation SignPath>'
          project-slug: 'winlestia'
          signing-policy-slug: 'release'
          artifact-configuration-slug: 'installer'
          github-artifact-id: ${{ steps.up-setup.outputs.artifact-id }}
          wait-for-completion: true
          output-artifact-directory: out/signed
```

Les entrées `wait-for-completion` et `output-artifact-directory`, l'horodatage automatique par SignPath et la possibilité d'enchaîner deux demandes dans un même job : **[non vérifiés]**, à contrôler dans https://docs.signpath.io/trusted-build-systems/github. Chaque demande attend l'approbation manuelle dans SignPath.

### 6.3 Repli : Certum Open Source (SimplySign) en local

1. Commander sur shop.certum.eu, activer le certificat, envoyer les pièces (identité, justificatif de domicile, URL du dépôt).
2. Installer SimplySign Desktop et se connecter avec le code de l'appli mobile : le certificat apparaît dans le magasin personnel Windows.
3. Récupérer l'empreinte SHA-1 du certificat (`certutil -user -store My`), puis signer (signtool du Windows SDK) :

```powershell
$tsa = 'http://time.certum.pl'          # horodateur RFC 3161 de Certum [URL à confirmer sur le site Certum]
$thumb = '<empreinte SHA-1 du certificat>'
# 1. payload : fichiers propres au projet uniquement
Get-ChildItem out\payload -Recurse -Include Winlestia*.exe,Winlestia*.dll,<dll natives> |
  ForEach-Object {
    signtool sign /sha1 $thumb /fd sha256 /tr $tsa /td sha256 /d "Winlestia" /du "https://github.com/lilidoxdev/winlestia" $_.FullName
  }
# 2. construire WinlestiaSetup.exe à partir du payload signé, puis :
signtool sign /sha1 $thumb /fd sha256 /tr $tsa /td sha256 /d "Winlestia Setup" /du "https://github.com/lilidoxdev/winlestia" out\WinlestiaSetup.exe
signtool verify /pa /all /v out\WinlestiaSetup.exe
```

4. Publier sur GitHub Releases les fichiers **déjà signés**, avec leurs empreintes SHA-256 dans les notes de version.

---

## 7. Signature de code ≠ licence produit

Les deux notions sont souvent confondues. Elles répondent à des questions différentes.

| | Signature de code (Authenticode) | Licence produit / activation |
|---|---|---|
| Question | **Qui** a publié ce fichier, et a-t-il été **modifié** depuis ? | **Qui a le droit** d'utiliser, copier ou modifier le logiciel, et à quelles conditions ? |
| Mécanisme | Certificat X.509 émis par une AC après vérification d'identité ; signature cryptographique du hash ; horodatage | Texte juridique (contrat, licence) ; éventuellement clé d'activation ou serveur de licences qui restreint l'exécution |
| Bénéficiaire | L'utilisateur, qui peut vérifier l'origine et l'intégrité ; Windows (SmartScreen, Smart App Control) | L'éditeur, qui contrôle les droits d'usage, souvent pour être payé |
| Effet sur Windows | « Éditeur : <nom> » au lieu de « inconnu » ; réputation SmartScreen | Aucun |

Un certificat ne donne et ne retire aucun droit d'utilisation. Une clé d'activation ne prouve ni l'identité de l'éditeur ni l'intégrité du binaire.

**Faut-il un système de licence ou d'activation pour Winlestia ? Non.**

- Le fichier `LICENSE` du dépôt contient la **GNU GPL version 3** (29 juin 2007), et `THIRD_PARTY_NOTICES.md` confirme « Licence de Winlestia : GPL-3.0 ». Ce texte accorde déjà à tout destinataire le droit d'exécuter, d'étudier, de modifier et de redistribuer le logiciel. Il n'y a rien à vendre ni à contrôler.
- La GPL interdit d'imposer des restrictions supplémentaires aux destinataires (articles 7 et 10). Un mécanisme d'activation pourrait être retiré légalement par n'importe qui dans une version modifiée. Il n'aurait donc aucun effet, et il compliquerait la candidature à SignPath (logiciel respectueux de l'utilisateur).
- L'article 9 de la GPL précise qu'il n'est **pas nécessaire d'accepter la licence pour recevoir ou exécuter** le programme. L'écran « Licence » de l'installateur doit donc **informer** (texte GPL-3.0 et lien vers `THIRD_PARTY_NOTICES.md`) et ne pas conditionner l'installation à une case « J'accepte ». Un simple bouton « Suivant » suffit.

**Obligations envers Caelestia (projet amont).** Le dépôt `caelestia-dots/shell` est sous **GPL-3.0** (fichier `LICENSE` lu sur GitHub le 17/11/2026). Winlestia en est un portage (v1.4.0, voir `THIRD_PARTY_NOTICES.md` et `src/backend/LICENSE-CAELESTIA.txt`), donc une œuvre dérivée. Obligations :

1. Distribuer Winlestia sous GPL-3.0 (c'est le cas) et joindre le texte de la licence à chaque distribution, installateur compris.
2. Conserver les mentions de copyright de Caelestia et de ses contributeurs (fait dans `THIRD_PARTY_NOTICES.md` ; à reprendre dans l'écran Licence ou dans un fichier installé).
3. Signaler les modifications et leur date (article 5a). C'est couvert par `UPSTREAM.json` / `PORT_PATCHES.json` et l'historique git.
4. Fournir le **code source correspondant** à chaque binaire publié (article 6). Le tag git de la version sur GitHub suffit, à condition qu'il permette réellement de reconstruire le payload. Un workflow de release sur GitHub Actions, utile pour SignPath, rend aussi cette correspondance vérifiable.
5. La signature de code est compatible avec la GPL-3.0. L'obligation de fournir des « Installation Information » (article 6) ne vise que les « User Products » (appareils grand public) et ne s'applique pas à un logiciel distribué pour PC. Vous n'avez pas à publier votre clé de signature.

Points connexes à traiter séparément : la licence des fonds d'écran (`assets/wallpapers/`, non établie) et les licences des thèmes Clink tiers marquées « non vérifiées » dans `THIRD_PARTY_NOTICES.md`.

---

## 8. Points non vérifiés (récapitulatif)

- Prix exact d'Azure Artifact Signing en euros (la page de tarification Azure affiche des valeurs vides le 17/11/2026 ; seul le « ~9,99 $/mois » de Microsoft Learn est disponible).
- Éligibilité d'une micro-entreprise française comme « organization » chez Azure.
- Délai d'examen SignPath ; possibilité pour un développeur seul de tenir tous les rôles ; partage éventuel de réputation SmartScreen entre projets signés par la fondation ; entrées exactes de l'action GitHub au-delà des entrées obligatoires.
- Prix de renouvellement Certum, liste exacte des pièces d'identité, affichage « à partir de 359 € » sur une page de la boutique, URL de l'horodateur (`http://time.certum.pl`, valeur habituelle mais non relue sur le site).
- Prix d'un certificat IV chez SSL.com ou Sectigo pour un particulier, et pièces demandées.
- Acceptation par le Store d'un remplaçant de barre des tâches en MSIX avec `runFullTrust`.
- État de signature des DLL Qt/PySide6 embarquées.
- Comportement de SmartScreen pour un installateur téléchargé par winget.
- Taux de conversion dollar/euro (approximation, non vérifié).

---

## 9. Sources (consultées le 17/11/2026)

- Microsoft Learn, Code signing options for Windows app developers (ms.date 29/10/2026) : https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options
- Microsoft Learn, SmartScreen reputation : https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation
- Microsoft Learn, Quickstart: Set up Artifact Signing (ms.date 21/05/2026) : https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart
- Azure, Artifact Signing pricing (montants non affichés) : https://azure.microsoft.com/en-us/pricing/details/artifact-signing/
- Microsoft Q&A, particulier français refusé (mars 2026) : https://learn.microsoft.com/en-us/answers/questions/5810735/cant-create-a-new-trusted-signing-individual-ident
- DevClass, GA d'Artifact Signing (14/01/2026) : https://www.devclass.com/security/2026/01/14/code-signing-windows-apps-may-be-easier-and-more-secure-with-new-azure-artifact-service/4079554
- Forum JUCE (témoignage, source secondaire) : https://forum.juce.com/t/azure-code-signing-for-plugin-developers-guide/60391?page=6
- Microsoft Q&A, citation du programme Trusted Root (EV, août 2024) : https://learn.microsoft.com/en-us/answers/questions/1846647/program-requirements-microsoft-trusted-root-progra
- Microsoft Learn, App package requirements for MSI/EXE app : https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements
- Windows Developer Blog, inscription gratuite des développeurs individuels (10/09/2025) : https://blogs.windows.com/windowsdeveloper/2025/09/10/free-developer-registration-for-individual-developers-on-microsoft-store/
- Microsoft Q&A, `runFullTrust` capacité restreinte : https://learn.microsoft.com/en-us/answers/questions/1424817/the-following-restricted-capabilities-require-appr
- Microsoft, soumission de fichiers (Defender) : https://www.microsoft.com/wdsi/filesubmission
- winget-pkgs, Policies : https://github.com/microsoft/winget-pkgs/blob/master/doc/Policies.md
- SignPath Foundation, conditions : https://signpath.org/terms ; candidature : https://signpath.org/apply ; accueil : https://signpath.org/
- SignPath, intégration GitHub : https://docs.signpath.io/trusted-build-systems/github
- Certum, Open Source Code Signing on SimplySign (49 € TTC) : https://shop.certum.eu/open-source-code-signing-on-simplysign.html
- Certum, Open Source Code Signing set (69 € TTC) : https://shop.certum.eu/open-source-code-signing.html
- Certum, pièces requises : https://support.certum.eu/en/code-signing-required-documents/
- Durée 460 jours (CSC-31) : https://www.encryptionconsulting.com/new-code-signing-certificate-validity-change/ ; GlobalSign : https://support.globalsign.com/customer/en/portal/articles/1464693-maximum-certificate-validity ; SSL.com : https://www.ssl.com/article/reminder-reduced-code-signing-certificate-lifecycles/amp/
- SSL.com, tarifs eSigner : https://ssl.com/guide/esigner-pricing-for-code-signing/
- Caelestia Shell, licence GPL-3.0 : https://github.com/caelestia-dots/shell/blob/main/LICENSE
- GNU GPL v3 (articles 5, 6, 7, 9, 10) : https://www.gnu.org/licenses/gpl-3.0.html
