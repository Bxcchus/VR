# Modern Villa XR

Prototype de visite immobilière en réalité virtuelle pour Meta Quest 3, créé avec Unity 6, OpenXR et XR Interaction Toolkit.

La scène de démarrage est [MainMenu.unity](Assets/CampusExplorer/Scenes/MainMenu.unity). Elle propose une visite libre, une visite guidée en six étapes et des pages Controls, Comfort et Credits. La visite se déroule dans [ModernVillaTour.unity](Assets/CampusExplorer/Scenes/ModernVillaTour.unity) : déplacement et téléportation XR, six points d’intérêt, portes, interrupteurs et petits objets saisissables.

## Ouvrir et essayer le projet

1. Installer **Git LFS** et exécuter `git lfs pull` après le clone : le FBX principal et le HDRI sont stockés via LFS.
2. Ouvrir ce dossier avec **Unity 6000.6.0f1** et attendre la restauration des packages et l’import des ressources.
3. Ouvrir `Assets/CampusExplorer/Scenes/MainMenu.unity`, puis lancer **Play**. Pour tester directement la villa, ouvrir `ModernVillaTour.unity`.
4. Avec Quest Link, activer le runtime OpenXR du casque avant Play. Le XR Interaction Simulator permet aussi un essai dans l’éditeur sans casque.

Le stick gauche déplace le joueur ; le stick droit le tourne. Le rayon et la gâchette index sélectionnent les éléments interactifs. Le bouton Menu de la manette gauche ouvre le menu des pièces ; Grip permet de saisir un objet. La page **Controls** du menu principal présente les commandes Quest Touch.

Les dépendances sont déclarées dans [Packages/manifest.json](Packages/manifest.json) et verrouillées dans [Packages/packages-lock.json](Packages/packages-lock.json). Les scènes activées dans les Build Settings sont MainMenu (index 0) et ModernVillaTour (index 1).

## Tests et build Quest 3

Les tests PlayMode C# sont dans [Assets/CampusExplorer/Tests/PlayMode](Assets/CampusExplorer/Tests/PlayMode). Dans Unity, ouvrir **Window > General > Test Runner**, choisir **PlayMode** et lancer les tests de la villa et du menu. Les tests des anciennes scènes CampusTour et Notre-Dame sont conservés comme archives et ne font pas partie des scènes de build actives.

Installer **Android Build Support** avec SDK, NDK et OpenJDK depuis Unity Hub. Dans Unity, lancer **Campus Explorer > Quest 3 > Audit prerequisites**, puis **Campus Explorer > Quest 3 > Build APK**. Le script [Quest3BuildTools.cs](Assets/CampusExplorer/Editor/Quest3BuildTools.cs) produit l’APK localement dans `Builds/Quest3/`. Les builds, caches Unity, rapports de test et captures ne sont pas versionnés. Un build autonome doit encore être vérifié physiquement sur Quest 3 après toute modification finale ; une réussite sous Quest Link en mode PC VR ne suffit pas à cette vérification.

## Sources et crédits

La villa et son mobilier proviennent de **Modern Luxury Villa Furnished and Animated Blender 5**, créé par **acrts076** et publié sur [CGTrader, modèle #6876788](https://www.cgtrader.com/free-3d-models/architectural/other/modern-luxury-villa-furnished-and-animated-blender-5). Les droits de redistribution des fichiers source FBX et textures doivent être vérifiés avant toute publication du dépôt qui les contient. Le projet ne requiert pas les scripts de préparation locaux du dossier `Tools/` pour s’ouvrir, lancer les tests ou construire l’APK.
