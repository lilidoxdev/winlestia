// Les tests partagent de l'etat global (Store.State, WINLESTIA_DATA_DIR, registre sandbox) : pas de parallelisme.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
